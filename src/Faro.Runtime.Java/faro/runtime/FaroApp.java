package faro.runtime;

import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.IOException;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.net.URL;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Properties;
import java.util.function.Consumer;
import java.util.regex.Pattern;
import javafx.application.Application;
import javafx.beans.property.Property;
import javafx.beans.property.SimpleBooleanProperty;
import javafx.event.ActionEvent;
import javafx.scene.Node;
import javafx.scene.Scene;
import javafx.scene.control.ButtonBase;
import javafx.scene.control.Labeled;
import javafx.scene.control.TextArea;
import javafx.scene.control.TextInputControl;
import javafx.scene.layout.Region;
import javafx.scene.layout.StackPane;
import javafx.stage.Stage;
import javax.xml.parsers.DocumentBuilderFactory;
import org.w3c.dom.Element;

/**
 * The runtime binder for Java projects: shows UIGraph screens with JavaFX and resolves &lt;Bind&gt; targets
 * ("pkg.Class.member") against the user's classes with reflection. The generated Main calls {@link #run}.
 * Bind event/prop names are the framework-neutral ones (Click, Text, …); {@link #event} / {@link #property} map them to JavaFX.
 */
public final class FaroApp {
    private FaroApp() { }

    private static File root;
    private static Class<?> mainClass;
    private static String appName;
    private static Stage stage;
    private static final Map<String, Element> screens = new LinkedHashMap<>();
    private static final Map<String, Element> components = new LinkedHashMap<>();
    private static final Map<String, List<Element>> binds = new HashMap<>();
    private static final Map<Class<?>, Object> singletons = new HashMap<>();
    private static Map<Class<?>, Object> screenScoped = new HashMap<>();

    /** Starts the app on faro.json's "startScreen". The project folder is the working directory (or -Dfaro.root). */
    public static void run(String[] args, Class<?> main) {
        mainClass = main;
        root = new File(System.getProperty("faro.root", ".")).getAbsoluteFile();
        var bundled = FaroApp.class.getResource("/faro/index.txt");
        if (bundled != null && !new File(root, "faro.json").isFile()) // an Android APK built by Faro
            try (var in = bundled.openStream()) { index = new String(in.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8).lines().filter(l -> !l.isBlank()).toList(); }
            catch (IOException e) { throw new IllegalStateException("Faro: can't read the bundled project: " + e.getMessage(), e); }
        UiBuilder.files = FaroApp::url;
        var meta = read("faro.json");
        appName = value(meta, "name", "FaroApp");
        start = value(meta, "startScreen", "MainScreen");
        load();
        UiBuilder.scriptFactory = name -> {
            try {
                var type = Class.forName(name, true, mainClass.getClassLoader());
                if (!FaroScript.class.isAssignableFrom(type)) throw new IllegalStateException("'" + name + "' is not a FaroScript class.");
                return ((FaroScript) instanceOf(type)).build();
            } catch (ClassNotFoundException e) {
                throw new IllegalStateException("'" + name + "' is not a class of this app.", e);
            }
        };
        Application.launch(RuntimeApp.class, args);
        release(screenScoped.values());
        release(singletons.values());
    }

    private static String start;

    /** The app window. The editor writes .faro/design.css for Material 3 (colors from the seed); Fluent keeps JavaFX's own look. */
    public static final class RuntimeApp extends Application {
        @Override
        public void start(Stage primary) {
            stage = primary;
            var scene = new Scene(new StackPane(), 480, 720);
            if (url(".faro/design.css") instanceof URL css) scene.getStylesheets().add(css.toExternalForm());
            stage.setScene(scene);
            stage.show();
            navigate(start);
        }
    }

    private static Object parameter;

    /** What the last navigate passed: the new screen's binds to members of its class use it (e.g. the tapped row's item). */
    public static Object parameter() { return parameter; }

    /** Opens a screen by UIGraph id. */
    public static void navigate(String screenId) { navigate(screenId, null); }

    /** Opens a screen with a value for its binds (spec §7: user code navigates with parameters). */
    public static void navigate(String screenId, Object parameter) {
        var graph = screens.get(screenId);
        if (graph == null) {
            showError("Navigate: screen '" + screenId + "' does not exist.");
            return;
        }
        release(screenScoped.values());
        screenScoped = new HashMap<>();
        FaroApp.parameter = parameter;
        var byId = new HashMap<String, Node>();
        var rootNode = UiBuilder.children(graph, "Node").get(0);
        var content = UiBuilder.build(rootNode, byId, root, "");
        if (content instanceof Region region) region.setMaxSize(Double.MAX_VALUE, Double.MAX_VALUE); // the screen fills the window
        var pane = new StackPane(content);
        pane.getStyleClass().add("faro-screen");
        stage.getScene().setRoot(pane);
        stage.setTitle(screenId);

        // Screen binds, then component-level binds (Bindings/<ComponentId>.xml) inside every instance of that component: {key, bind}.
        var all = new ArrayList<Object[]>();
        for (var b : binds.getOrDefault(screenId, List.of())) all.add(new Object[] { b.getAttribute("nodeId"), b });
        for (var path : UiBuilder.instancePaths(rootNode, ""))
            for (var b : binds.getOrDefault(path[1], List.of())) all.add(new Object[] { path[0] + b.getAttribute("nodeId"), b });
        var lists = byId.entrySet().stream().filter(e -> e.getValue() instanceof UiBuilder.RepeatHost).map(e -> e.getKey() + "/").toList();
        var errors = new ArrayList<String>();
        for (var pair : all) {
            var key = (String) pair[0];
            var b = (Element) pair[1];
            var control = byId.get(key);
            if (control == null || lists.stream().anyMatch(key::startsWith)) continue; // binds inside a list apply per row (bindItems)
            if (control instanceof UiBuilder.RepeatHost list && "Items".equals(b.getAttribute("prop")))
                tryApply(errors, b, () -> bindItems(list, b, key, all));
            else tryApply(errors, b, () -> apply(b, control, null));
        }
        if (!errors.isEmpty()) showError(String.join("\n\n", errors));
    }

    /** "Navigate:Screen.Detail" → "Detail" (the "Screen." prefix is optional). */
    public static String navigateScreenId(String target) {
        var id = target.substring("Navigate:".length());
        return !screens.containsKey(id) && id.startsWith("Screen.") ? id.substring("Screen.".length()) : id;
    }

    private interface Binding { void run() throws ReflectiveOperationException; }

    private static void tryApply(List<String> errors, Element bind, Binding binding) {
        try { binding.run(); }
        catch (RuntimeException | ReflectiveOperationException e) { errors.add(describe(bind) + "\n  → " + e.getMessage()); }
    }

    /**
     * prop="Items" on a repeatable instance: one copy of it per item of a list property (Iterable), redrawn when
     * the owner calls changed("&lt;property&gt;"). Binds inside a row use the row's item when the target's class is the item's type.
     */
    private static void bindItems(UiBuilder.RepeatHost list, Element bind, String key, List<Object[]> all) throws ReflectiveOperationException {
        var target = bind.getAttribute("target");
        var name = memberOf(target);
        var getter = getter(classOf(target), name);
        if (!Iterable.class.isAssignableFrom(getter.getReturnType())) throw new IllegalStateException("'" + target + "' is not a list.");
        var owner = Modifier.isStatic(getter.getModifiers()) ? null : instanceOf(getter.getDeclaringClass());
        var prefix = key.substring(0, key.length() - list.node.getAttribute("id").length());
        var screen = stage.getScene().getRoot();
        Runnable render = () -> {
            if (stage.getScene().getRoot() != screen) return; // a singleton owner outlives the screen: stop once it is left
            var errors = new ArrayList<String>();
            list.rows().clear();
            try {
                var items = (Iterable<?>) getter.invoke(owner);
                if (items != null) for (var item : items) {
                    var row = new HashMap<String, Node>();
                    list.rows().add(UiBuilder.copy(list.node, row, root, prefix));
                    for (var pair : all) {
                        var control = row.get((String) pair[0]);
                        if (((String) pair[0]).startsWith(key + "/") && control != null) tryApply(errors, (Element) pair[1], () -> apply((Element) pair[1], control, item));
                    }
                }
            } catch (ReflectiveOperationException e) { errors.add(target + ": " + e.getCause()); }
            if (!errors.isEmpty()) showError(String.join("\n\n", errors));
        };
        render.run();
        if (owner instanceof FaroObject observable) observable.addChangeListener(changed -> { if (changed.equals(name)) render.run(); });
    }

    /** Members of the row item's class (inside a list) or of the navigation parameter's class bind to that object. */
    private static Object given(Class<?> type, Object item) { return type.isInstance(item) ? item : type.isInstance(parameter) ? parameter : null; }

    private static Object source(Class<?> type, Object item) { var given = given(type, item); return given != null ? given : instanceOf(type); }

    private static void apply(Element bind, Node control, Object item) throws ReflectiveOperationException {
        var target = bind.getAttribute("target");
        if (target.isEmpty()) throw new IllegalStateException("Bind has no target.");
        if (bind.hasAttribute("event")) {
            var hook = event(control, bind.getAttribute("event"));
            if (target.startsWith("Navigate:")) {
                var screen = navigateScreenId(target);
                if (!screens.containsKey(screen)) throw new IllegalStateException("Screen '" + screen + "' does not exist.");
                hook.accept(() -> navigate(screen, item)); // a row passes its item
                return;
            }
            // No parameters, or one: the row's item (selection) or the navigation parameter, whichever its type takes.
            Method method = null;
            for (var m : classOf(target).getMethods())
                if (m.getName().equals(memberOf(target)) && m.getParameterCount() <= 1 && (method == null || m.getParameterCount() == 0)) method = m;
            if (method == null) throw new NoSuchMethodException("'" + target + "' must be a public method with no parameters, or one (the row's item or the screen's parameter).");
            Object[] args = {};
            if (method.getParameterCount() == 1) {
                var arg = given(method.getParameterTypes()[0], item);
                if (arg == null) throw new IllegalStateException("'" + target + "' takes a " + method.getParameterTypes()[0].getSimpleName() + ": bind it inside a list of them, or on a screen opened with one.");
                args = new Object[] { arg };
            }
            var chosen = method;
            var arguments = args;
            hook.accept(() -> {
                try { chosen.invoke(Modifier.isStatic(chosen.getModifiers()) ? null : source(chosen.getDeclaringClass(), item), arguments); }
                catch (InvocationTargetException e) { showError(target + " threw:\n" + e.getCause()); }
                catch (IllegalAccessException e) { showError(target + ": " + e.getMessage()); }
            });
        } else {
            @SuppressWarnings("unchecked")
            var fx = (Property<Object>) property(control, bind.getAttribute("prop"));
            var type = classOf(target);
            var name = memberOf(target);
            var getter = getter(type, name);
            var source = source(type, item);
            var format = bind.getAttribute("format");
            Runnable pull = () -> {
                try { fx.setValue(format.isEmpty() ? convert(getter.invoke(source), fx.getValue()) : format(format, getter.invoke(source))); }
                catch (ReflectiveOperationException e) { showError(target + ": " + e.getCause()); }
            };
            pull.run();
            if (source instanceof FaroObject observable) observable.addChangeListener(changed -> { if (changed.equals(name)) pull.run(); });
            if ("TwoWay".equals(bind.getAttribute("mode"))) {
                var setter = setter(type, name, getter.getReturnType());
                fx.addListener((o, before, after) -> {
                    try { setter.invoke(source, after); }
                    catch (ReflectiveOperationException e) { showError(target + ": " + e.getCause()); }
                });
            }
        }
    }

    /** Neutral event names → JavaFX. */
    private static Consumer<Runnable> event(Node control, String name) {
        if (name.equals("Click") && control instanceof ButtonBase button)
            return r -> button.addEventHandler(ActionEvent.ACTION, e -> r.run());
        if (name.equals("Click") && control instanceof javafx.scene.layout.Pane pane) // containers: a tap (e.g. a list row)
            return r -> pane.addEventHandler(javafx.scene.input.MouseEvent.MOUSE_CLICKED, e -> r.run());
        if (name.equals("Changed") && control instanceof TextInputControl input)
            return r -> input.textProperty().addListener((o, a, b) -> r.run());
        throw new IllegalStateException(control.getClass().getSimpleName() + " has no event '" + name + "'.");
    }

    /** Neutral property names → JavaFX. */
    private static Property<?> property(Node control, String name) {
        Property<?> property = switch (name) {
            case "Text" -> control instanceof Labeled l ? l.textProperty() : control instanceof TextInputControl t ? t.textProperty() : null;
            case "Placeholder" -> control instanceof TextInputControl t ? t.promptTextProperty() : null;
            case "Visible" -> control.visibleProperty();
            case "Enabled" -> {
                var enabled = new SimpleBooleanProperty(!control.isDisable());
                control.disableProperty().bind(enabled.not());
                yield enabled;
            }
            default -> null;
        };
        if (property == null) throw new IllegalStateException(control.getClass().getSimpleName() + " has no property '" + name + "'.");
        return property;
    }

    private static final Pattern FORMAT = Pattern.compile("\\{0(?::([NF])(\\d*))?\\}");

    /** "¥{0:N0}" as in .NET: {0}, {0:N<decimals>} (thousands separators) and {0:F<decimals>} are the portable ones. */
    static String format(String format, Object value) {
        return FORMAT.matcher(format).replaceAll(m -> java.util.regex.Matcher.quoteReplacement(m.group(1) == null || !(value instanceof Number number) ? String.valueOf(value)
            : String.format((m.group(1).equals("N") ? "%,." : "%.") + (m.group(2).isEmpty() ? "2" : m.group(2)) + "f", number.doubleValue())));
    }

    private static Object convert(Object value, Object current) {
        return current instanceof Boolean || value instanceof Boolean ? Boolean.TRUE.equals(value) : value == null ? "" : String.valueOf(value);
    }

    private static Class<?> classOf(String target) throws ClassNotFoundException {
        var dot = target.lastIndexOf('.');
        if (dot <= 0) throw new ClassNotFoundException("'" + target + "' has no class part.");
        try { return Class.forName(target.substring(0, dot), true, mainClass.getClassLoader()); }
        catch (ClassNotFoundException e) { throw new ClassNotFoundException("Class '" + target.substring(0, dot) + "' not found."); }
    }

    private static String memberOf(String target) { return target.substring(target.lastIndexOf('.') + 1); }

    /** Bean property "name": getName() or isName(). */
    private static Method getter(Class<?> type, String name) throws NoSuchMethodException {
        var cap = Character.toUpperCase(name.charAt(0)) + name.substring(1);
        for (var prefix : new String[] { "get", "is" })
            try { return type.getMethod(prefix + cap); } catch (NoSuchMethodException ignored) { }
        throw new NoSuchMethodException("'" + type.getSimpleName() + "' has no public property '" + name + "' (get" + cap + "()).");
    }

    private static Method setter(Class<?> type, String name, Class<?> valueType) throws NoSuchMethodException {
        return type.getMethod("set" + Character.toUpperCase(name.charAt(0)) + name.substring(1), valueType);
    }

    private static Object instanceOf(Class<?> type) {
        var lifetime = type.getAnnotation(FaroLifetime.class);
        var store = lifetime == null ? screenScoped : switch (lifetime.value()) {
            case TRANSIENT -> null;
            case SINGLETON -> singletons;
            case SCREEN_SCOPED -> screenScoped;
        };
        if (store != null && store.containsKey(type)) return store.get(type);
        try {
            var created = type.getConstructor().newInstance();
            if (lifetime != null && lifetime.persistent()) restore(created);
            if (store != null) store.put(type, created);
            return created;
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException("Can't create " + type.getName() + ": " + e, e);
        }
    }

    /** Persistent classes keep their simple bean properties (text, numbers, booleans) in a .properties file. */
    private static void restore(Object o) throws ReflectiveOperationException {
        var file = persistFile(o.getClass());
        if (!file.isFile()) return;
        var saved = new Properties();
        try (var in = new FileReader(file, java.nio.charset.StandardCharsets.UTF_8)) { saved.load(in); }
        catch (IOException e) { return; }
        for (var m : o.getClass().getMethods()) {
            if (!m.getName().startsWith("set") || m.getParameterCount() != 1) continue;
            var text = saved.getProperty(m.getName().substring(3));
            if (text == null) continue;
            var t = m.getParameterTypes()[0];
            Object value = t == String.class ? text : t == int.class ? Integer.valueOf(text) : t == long.class ? Long.valueOf(text)
                : t == double.class ? Double.valueOf(text) : t == boolean.class ? Boolean.valueOf(text) : null;
            if (value != null) m.invoke(o, value);
        }
    }

    private static void release(Iterable<Object> instances) {
        for (var o : instances) {
            var lifetime = o.getClass().getAnnotation(FaroLifetime.class);
            if (lifetime != null && lifetime.persistent()) {
                var saved = new Properties();
                for (var m : o.getClass().getMethods())
                    if (m.getName().startsWith("get") && m.getParameterCount() == 0 && m.getDeclaringClass() != Object.class && (m.getReturnType().isPrimitive() || m.getReturnType() == String.class))
                        try { saved.setProperty(m.getName().substring(3), String.valueOf(m.invoke(o))); } catch (ReflectiveOperationException ignored) { }
                var file = persistFile(o.getClass());
                file.getParentFile().mkdirs();
                try (var out = new FileWriter(file, java.nio.charset.StandardCharsets.UTF_8)) { saved.store(out, "Faro"); }
                catch (IOException e) { System.err.println("Faro: couldn't save " + file + ": " + e.getMessage()); }
            }
            if (o instanceof AutoCloseable closeable) try { closeable.close(); } catch (Exception ignored) { }
        }
    }

    private static File persistFile(Class<?> type) {
        var appData = System.getenv("APPDATA") != null ? System.getenv("APPDATA") : System.getProperty("user.home") + "/.config";
        return new File(appData, "Faro/" + appName + "/" + type.getName() + ".properties");
    }

    /** Binding/runtime errors in a small window whose text can be selected and copied (spec §6). */
    private static void showError(String text) {
        var area = new TextArea(text);
        area.setEditable(false);
        area.setWrapText(true);
        area.setStyle("-fx-font-family: monospace;");
        var window = new Stage();
        window.setTitle("Faro: binding error");
        window.setScene(new Scene(area, 640, 320));
        if (stage != null && stage.isShowing()) window.initOwner(stage);
        window.show();
    }

    private static String describe(Element bind) {
        var attributes = bind.getAttributes();
        var text = new StringBuilder("<Bind");
        for (var i = 0; i < attributes.getLength(); i++) text.append(' ').append(attributes.item(i).getNodeName()).append("=\"").append(attributes.item(i).getNodeValue()).append('"');
        return text.append(" />").toString();
    }

    /** UI/*.xml (UIGraph screens, ComponentDef components) and Bindings/*.xml (per screen or component id). */
    private static void load() {
        try {
            var factory = DocumentBuilderFactory.newInstance();
            for (var name : xml("UI"))
                try (var in = url("UI/" + name).openStream()) {
                    var doc = factory.newDocumentBuilder().parse(in).getDocumentElement();
                    (doc.getTagName().equals("ComponentDef") ? components : screens).put(doc.getAttribute("id"), doc);
                }
            for (var name : xml("Bindings"))
                try (var in = url("Bindings/" + name).openStream()) {
                    binds.put(name.substring(0, name.length() - 4), UiBuilder.children(factory.newDocumentBuilder().parse(in).getDocumentElement(), "Bind"));
                }
        } catch (Exception e) {
            throw new IllegalStateException("Faro: can't load the project in " + root + ": " + e.getMessage(), e);
        }
    }

    /**
     * Project files (faro.json, UI/, Bindings/, Assets/, .faro/design.css): from the project folder, or, in an Android APK
     * (no folder), from the resources Faro packs under /faro/ with an index.txt listing them. Null when missing.
     */
    static URL url(String path) {
        if (index != null) return FaroApp.class.getResource("/faro/" + path);
        var file = new File(root, path);
        try { return file.isFile() ? file.toURI().toURL() : null; } catch (java.net.MalformedURLException e) { return null; }
    }

    private static List<String> index; // bundled project files (Android), null for a project folder

    private static List<String> xml(String dir) {
        if (index != null)
            return index.stream().filter(p -> p.startsWith(dir + "/") && p.endsWith(".xml")).map(p -> p.substring(dir.length() + 1)).sorted().toList();
        var names = new File(root, dir).list((d, n) -> n.endsWith(".xml"));
        return names == null ? List.of() : java.util.Arrays.stream(names).sorted().toList();
    }

    private static String read(String path) {
        var url = url(path);
        if (url == null) return "";
        try (var in = url.openStream()) { return new String(in.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8); }
        catch (IOException e) { return ""; }
    }

    /** ponytail: a flat "key": "value" lookup; faro.json is Faro-written and simple. Use a JSON library if it grows. */
    private static String value(String json, String key, String fallback) {
        var m = Pattern.compile("\"" + key + "\"\\s*:\\s*\"([^\"]*)\"").matcher(json);
        return m.find() ? m.group(1) : fallback;
    }
}

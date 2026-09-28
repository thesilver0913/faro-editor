package faro.runtime;

import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.IOException;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.nio.file.Files;
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
        var meta = read(new File(root, "faro.json"));
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
            var css = new File(root, ".faro/design.css");
            if (css.isFile()) scene.getStylesheets().add(css.toURI().toString());
            stage.setScene(scene);
            stage.show();
            navigate(start);
        }
    }

    /** Opens a screen by UIGraph id. Also the API user code calls for parameterised navigation (spec §7). */
    public static void navigate(String screenId) {
        var graph = screens.get(screenId);
        if (graph == null) {
            showError("Navigate: screen '" + screenId + "' does not exist.");
            return;
        }
        release(screenScoped.values());
        screenScoped = new HashMap<>();
        var byId = new HashMap<String, Node>();
        var rootNode = UiBuilder.children(graph, "Node").get(0);
        var content = UiBuilder.build(rootNode, byId, root, "");
        if (content instanceof Region region) region.setMaxSize(Double.MAX_VALUE, Double.MAX_VALUE); // the screen fills the window
        var pane = new StackPane(content);
        pane.getStyleClass().add("faro-screen");
        stage.getScene().setRoot(pane);
        stage.setTitle(screenId);

        var errors = new ArrayList<String>();
        Consumer<Object[]> bind = pair -> {
            var b = (Element) pair[0];
            var control = byId.get(pair[1] + b.getAttribute("nodeId"));
            if (control == null) return;
            try { apply(b, control); }
            catch (RuntimeException | ReflectiveOperationException e) { errors.add(describe(b) + "\n  → " + e.getMessage()); }
        };
        for (var b : binds.getOrDefault(screenId, List.of())) bind.accept(new Object[] { b, "" });
        // Component-level bindings (Bindings/<ComponentId>.xml) apply inside every instance of that component.
        for (var path : UiBuilder.instancePaths(rootNode, ""))
            for (var b : binds.getOrDefault(path[1], List.of())) bind.accept(new Object[] { b, path[0] });
        if (!errors.isEmpty()) showError(String.join("\n\n", errors));
    }

    /** "Navigate:Screen.Detail" → "Detail" (the "Screen." prefix is optional). */
    public static String navigateScreenId(String target) {
        var id = target.substring("Navigate:".length());
        return !screens.containsKey(id) && id.startsWith("Screen.") ? id.substring("Screen.".length()) : id;
    }

    private static void apply(Element bind, Node control) throws ReflectiveOperationException {
        var target = bind.getAttribute("target");
        if (target.isEmpty()) throw new IllegalStateException("Bind has no target.");
        if (bind.hasAttribute("event")) {
            var hook = event(control, bind.getAttribute("event"));
            if (target.startsWith("Navigate:")) {
                var screen = navigateScreenId(target);
                if (!screens.containsKey(screen)) throw new IllegalStateException("Screen '" + screen + "' does not exist.");
                hook.accept(() -> navigate(screen));
                return;
            }
            // ponytail: parameterless methods only; pass event data when a use case needs it
            var method = classOf(target).getMethod(memberOf(target));
            hook.accept(() -> {
                try { method.invoke(Modifier.isStatic(method.getModifiers()) ? null : instanceOf(method.getDeclaringClass())); }
                catch (InvocationTargetException e) { showError(target + " threw:\n" + e.getCause()); }
                catch (IllegalAccessException e) { showError(target + ": " + e.getMessage()); }
            });
        } else {
            @SuppressWarnings("unchecked")
            var fx = (Property<Object>) property(control, bind.getAttribute("prop"));
            var type = classOf(target);
            var name = memberOf(target);
            var getter = getter(type, name);
            var source = instanceOf(type);
            Runnable pull = () -> {
                try { fx.setValue(convert(getter.invoke(source), fx.getValue())); }
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
            for (var file : xml(new File(root, "UI"))) {
                var doc = factory.newDocumentBuilder().parse(file).getDocumentElement();
                (doc.getTagName().equals("ComponentDef") ? components : screens).put(doc.getAttribute("id"), doc);
            }
            for (var file : xml(new File(root, "Bindings"))) {
                var id = file.getName().substring(0, file.getName().length() - 4);
                binds.put(id, UiBuilder.children(factory.newDocumentBuilder().parse(file).getDocumentElement(), "Bind"));
            }
        } catch (Exception e) {
            throw new IllegalStateException("Faro: can't load the project in " + root + ": " + e.getMessage(), e);
        }
    }

    private static File[] xml(File dir) {
        var files = dir.listFiles((d, n) -> n.endsWith(".xml"));
        if (files == null) return new File[0];
        java.util.Arrays.sort(files);
        return files;
    }

    private static String read(File file) {
        try { return Files.readString(file.toPath()); } catch (IOException e) { return ""; }
    }

    /** ponytail: a flat "key": "value" lookup; faro.json is Faro-written and simple. Use a JSON library if it grows. */
    private static String value(String json, String key, String fallback) {
        var m = Pattern.compile("\"" + key + "\"\\s*:\\s*\"([^\"]*)\"").matcher(json);
        return m.find() ? m.group(1) : fallback;
    }
}

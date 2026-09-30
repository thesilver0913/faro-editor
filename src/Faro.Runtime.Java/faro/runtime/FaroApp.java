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
import java.util.Objects;
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
        UiBuilder.tokens = tokens(meta);
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
            loadFonts();
            var scene = new Scene(new StackPane(), 480, 720);
            scene.getStylesheets().add("data:text/css;base64," + java.util.Base64.getEncoder().encodeToString(UiBuilder.CSS.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
            if (url(".faro/design.css") instanceof URL css) scene.getStylesheets().add(css.toExternalForm());
            stage.setScene(scene);
            stage.show();
            navigate(start);
            watchLive(scene);
        }
    }

    /**
     * Live reload: when the editor runs the app it passes the project folder in FARO_LIVE. Saved changes to UI/, Bindings/,
     * faro.json (tokens) and .faro/design.css rebuild the current screen in place, with the same instances, so its state stays.
     */
    private static void watchLive(Scene scene) {
        var dir = System.getenv("FARO_LIVE");
        if (dir == null || dir.isEmpty() || index != null || !new File(dir).isDirectory()) return;
        var thread = new Thread(() -> {
            try (var service = java.nio.file.FileSystems.getDefault().newWatchService()) {
                for (var sub : new String[] { "", "UI", "Bindings", ".faro" }) {
                    var path = new File(root, sub).toPath();
                    if (path.toFile().isDirectory())
                        path.register(service, java.nio.file.StandardWatchEventKinds.ENTRY_CREATE, java.nio.file.StandardWatchEventKinds.ENTRY_MODIFY, java.nio.file.StandardWatchEventKinds.ENTRY_DELETE);
                }
                while (true) {
                    var key = service.take();
                    Thread.sleep(200); // editors save in bursts
                    key.pollEvents();
                    key.reset();
                    for (java.nio.file.WatchKey more; (more = service.poll()) != null; ) { more.pollEvents(); more.reset(); }
                    javafx.application.Platform.runLater(() -> reload(scene));
                }
            } catch (IOException | InterruptedException e) { /* live reload ends with the app */ }
        }, "faro-live");
        thread.setDaemon(true);
        thread.start();
    }

    private static void reload(Scene scene) {
        try {
            screens.clear();
            components.clear();
            binds.clear();
            UiBuilder.tokens = tokens(read("faro.json"));
            load();
            loadFonts();
            scene.getStylesheets().removeIf(css -> !css.startsWith("data:"));
            scene.getStylesheets().removeIf(css -> css.startsWith("data:text/css;charset=utf-8,")); // the last design.css
            var design = read(".faro/design.css");
            if (!design.isEmpty()) // as data: so JavaFX doesn't serve its cached copy; fonts resolve against the project folder
                scene.getStylesheets().add("data:text/css;charset=utf-8," + java.net.URLEncoder.encode(design.replace("url('fonts/", "url('" + new File(root, ".faro/fonts").toURI() + "/"), java.nio.charset.StandardCharsets.UTF_8).replace("+", "%20"));
            if (current != null && screens.get(current) instanceof Element graph) show(current, graph);
        } catch (RuntimeException e) {
            showError("Live reload: " + e.getMessage()); // a half-written file: the next save retries
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
        for (var o : singletons.values()) save(o); // also when the app is killed later (the editor's Stop)
        screenScoped = new HashMap<>();
        FaroApp.parameter = parameter;
        show(screenId, graph);
    }

    private static String current; // the screen shown (live reload rebuilds it)

    private static void show(String screenId, Element graph) {
        current = screenId;
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
        if (owner instanceof FaroObject observable) watch(observable, name, render);
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
                try { fx.setValue(format.isEmpty() ? convert(getter.invoke(source), fx) : format(format, getter.invoke(source))); }
                catch (ReflectiveOperationException e) { showError(target + ": " + e.getCause()); }
            };
            pull.run();
            if (source instanceof FaroObject observable) watch(observable, name, pull);
            if ("TwoWay".equals(bind.getAttribute("mode"))) {
                var setter = setter(type, name, getter.getReturnType());
                fx.addListener((o, before, after) -> {
                    try { setter.invoke(source, back(after, setter.getParameterTypes()[0])); }
                    catch (ReflectiveOperationException e) { showError(target + ": " + e.getCause()); }
                });
            }
        }
    }

    /** Runs <code>update</code> when the property changes, until the screen is left (a singleton outlives it: no stale listeners). */
    private static void watch(FaroObject observable, String property, Runnable update) {
        var screen = stage.getScene().getRoot();
        @SuppressWarnings("unchecked") Consumer<String>[] self = new Consumer[1];
        self[0] = changed -> {
            if (stage.getScene().getRoot() != screen) observable.removeChangeListener(self[0]);
            else if (changed.equals(property)) update.run();
        };
        observable.addChangeListener(self[0]);
    }

    /** Neutral event names → JavaFX. */
    private static Consumer<Runnable> event(Node control, String name) {
        if (name.equals("Click") && control instanceof ButtonBase button)
            return r -> button.addEventHandler(ActionEvent.ACTION, e -> r.run());
        if (name.equals("Click") && control instanceof javafx.scene.layout.Pane pane) // containers: a tap (e.g. a list row)
            return r -> pane.addEventHandler(javafx.scene.input.MouseEvent.MOUSE_CLICKED, e -> r.run());
        if (name.equals("Changed") && control instanceof TextInputControl input)
            return r -> input.textProperty().addListener((o, a, b) -> r.run());
        if (name.equals("Changed") && control instanceof javafx.scene.control.CheckBox box)
            return r -> box.selectedProperty().addListener((o, a, b) -> r.run());
        if (name.equals("Changed") && control instanceof javafx.scene.control.Spinner<?> spinner)
            return r -> spinner.valueProperty().addListener((o, a, b) -> r.run());
        if (name.equals("Changed") && control instanceof javafx.scene.control.Slider slider)
            return r -> slider.valueProperty().addListener((o, a, b) -> r.run());
        if (name.equals("Changed") && control instanceof javafx.scene.control.ComboBox<?> select)
            return r -> select.valueProperty().addListener((o, a, b) -> r.run());
        throw new IllegalStateException(control.getClass().getSimpleName() + " has no event '" + name + "'.");
    }

    /** Neutral property names → JavaFX. */
    private static Property<?> property(Node control, String name) {
        Property<?> property = switch (name) {
            case "Text" -> control instanceof Labeled l ? l.textProperty() : control instanceof TextInputControl t ? t.textProperty() : null;
            case "Checked" -> control instanceof javafx.scene.control.CheckBox box ? box.selectedProperty() : null;
            case "Value" -> control instanceof javafx.scene.control.Spinner<?> s ? spinnerValue(s)
                : control instanceof javafx.scene.control.Slider s ? s.valueProperty()
                : control instanceof javafx.scene.control.ProgressBar bar ? percent(bar) : null;
            case "Minimum" -> control instanceof javafx.scene.control.Slider s ? s.minProperty()
                : control instanceof javafx.scene.control.Spinner<?> s && s.getValueFactory() instanceof javafx.scene.control.SpinnerValueFactory.DoubleSpinnerValueFactory f ? f.minProperty() : null;
            case "Maximum" -> control instanceof javafx.scene.control.Slider s ? s.maxProperty()
                : control instanceof javafx.scene.control.Spinner<?> s && s.getValueFactory() instanceof javafx.scene.control.SpinnerValueFactory.DoubleSpinnerValueFactory f ? f.maxProperty() : null;
            case "Selected" -> {
                if (!(control instanceof javafx.scene.control.ComboBox<?> select)) yield null;
                @SuppressWarnings("unchecked") var value = (javafx.beans.property.ObjectProperty<String>) (Object) select.valueProperty();
                var text = new javafx.beans.property.SimpleStringProperty(value.get());
                text.bindBidirectional(value);
                yield text;
            }
            case "Options" -> {
                if (!(control instanceof javafx.scene.control.ComboBox<?> select)) yield null;
                @SuppressWarnings("unchecked") var items = (javafx.collections.ObservableList<String>) (Object) select.getItems();
                var list = new javafx.beans.property.SimpleObjectProperty<Object>();
                list.addListener((o, a, b) -> {
                    var strings = new ArrayList<String>();
                    if (b instanceof Iterable<?> each) for (var item : each) strings.add(String.valueOf(item));
                    items.setAll(strings);
                });
                yield list;
            }
            case "Placeholder" -> control instanceof TextInputControl t ? t.promptTextProperty()
                : control instanceof javafx.scene.control.Spinner<?> s ? s.getEditor().promptTextProperty()
                : control instanceof javafx.scene.control.DatePicker d ? d.promptTextProperty() : null;
            case "Foreground" -> UiBuilder.boundColor(control, true);
            case "Background" -> control instanceof javafx.scene.layout.Pane ? UiBuilder.boundColor(control, false) : null;
            case "Date" -> {
                if (!(control instanceof javafx.scene.control.DatePicker picker)) yield null;
                // The neutral "yyyy-MM-dd" text both ways (a bad text leaves the picker empty)
                var text = new javafx.beans.property.SimpleStringProperty(picker.getValue() == null ? "" : picker.getValue().toString());
                text.addListener((o, a, b) -> {
                    try { var date = b == null || b.isEmpty() ? null : java.time.LocalDate.parse(b.trim()); if (!Objects.equals(date, picker.getValue())) picker.setValue(date); }
                    catch (java.time.format.DateTimeParseException e) { picker.setValue(null); }
                });
                picker.valueProperty().addListener((o, a, b) -> text.set(b == null ? "" : b.toString()));
                yield text;
            }
            case "Icon" -> control instanceof IconSet.View icon ? icon.icon : null;
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

    /** A member's value for a JavaFX property: its type decides (a check, a number, a text, or the value itself: a list of options). */
    private static Object convert(Object value, Property<?> target) {
        if (target instanceof javafx.beans.property.BooleanProperty) return Boolean.TRUE.equals(value);
        if (target instanceof javafx.beans.property.DoubleProperty) return value instanceof Number n ? n.doubleValue() : 0.0;
        if (target instanceof javafx.beans.property.StringProperty) return value == null ? "" : String.valueOf(value);
        return value;
    }

    /** A control's value written back to a member of type <code>type</code> (a slider's double into an int property…). */
    private static Object back(Object value, Class<?> type) {
        if (value instanceof Number n) {
            if (type == int.class || type == Integer.class) return (int) Math.round(n.doubleValue());
            if (type == long.class || type == Long.class) return Math.round(n.doubleValue());
            if (type == double.class || type == Double.class) return n.doubleValue();
        }
        return type == String.class && value != null ? String.valueOf(value) : value;
    }

    /** A number input's Value as a double property, both ways (typed text counts once it parses). */
    private static Property<?> spinnerValue(javafx.scene.control.Spinner<?> spinner) {
        @SuppressWarnings("unchecked") var factory = (javafx.scene.control.SpinnerValueFactory<Double>) spinner.getValueFactory();
        var value = new javafx.beans.property.SimpleDoubleProperty(factory.getValue() == null ? 0 : factory.getValue());
        value.addListener((o, a, b) -> { if (!Objects.equals(factory.getValue(), b.doubleValue())) factory.setValue(b.doubleValue()); });
        factory.valueProperty().addListener((o, a, b) -> { if (b != null) value.set(b); });
        spinner.getEditor().textProperty().addListener((o, a, b) -> {
            try { value.set(Double.parseDouble(b.trim())); } catch (NumberFormatException ignored) { }
        });
        return value;
    }

    /** A progress bar's Value (0–100) as JavaFX's progress (0–1). */
    private static Property<?> percent(javafx.scene.control.ProgressBar bar) {
        var value = new javafx.beans.property.SimpleDoubleProperty(bar.getProgress() * 100);
        bar.progressProperty().bind(value.divide(100));
        return value;
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

    /** The instance bindings use for a class (by its lifetime): code reaches another class's singleton this way. */
    public static <T> T get(Class<T> type) { return type.cast(instanceOf(type)); }

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
            Object value;
            try {
                value = t == String.class ? text : t == int.class ? Integer.valueOf(text) : t == long.class ? Long.valueOf(text)
                    : t == double.class ? Double.valueOf(text) : t == boolean.class ? Boolean.valueOf(text) : null;
            } catch (NumberFormatException e) { continue; } // a damaged value: keep the default
            if (value != null) m.invoke(o, value);
        }
    }

    private static void release(Iterable<Object> instances) {
        for (var o : instances) {
            save(o);
            if (o instanceof AutoCloseable closeable) try { closeable.close(); } catch (Exception ignored) { }
        }
    }

    private static void save(Object o) {
        var lifetime = o.getClass().getAnnotation(FaroLifetime.class);
        if (lifetime != null && lifetime.persistent()) {
            var saved = new Properties();
            for (var m : o.getClass().getMethods())
                if ((m.getName().startsWith("get") || m.getName().startsWith("is") && m.getReturnType() == boolean.class) && m.getParameterCount() == 0
                    && m.getDeclaringClass() != Object.class && (m.getReturnType().isPrimitive() || m.getReturnType() == String.class))
                    try { saved.setProperty(m.getName().substring(m.getName().startsWith("is") ? 2 : 3), String.valueOf(m.invoke(o))); } catch (ReflectiveOperationException ignored) { }
            var file = persistFile(o.getClass());
            file.getParentFile().mkdirs();
            try (var out = new FileWriter(file, java.nio.charset.StandardCharsets.UTF_8)) { saved.store(out, "Faro"); }
            catch (IOException e) { System.err.println("Faro: couldn't save " + file + ": " + e.getMessage()); }
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

    private static List<String> xml(String dir) { return files(dir, ".xml"); }

    private static List<String> files(String dir, String... extensions) {
        java.util.function.Predicate<String> match = n -> java.util.Arrays.stream(extensions).anyMatch(e -> n.toLowerCase().endsWith(e));
        if (index != null)
            return index.stream().filter(p -> p.startsWith(dir + "/") && match.test(p)).map(p -> p.substring(dir.length() + 1)).sorted().toList();
        var names = new File(root, dir).list((d, n) -> match.test(n));
        return names == null ? List.of() : java.util.Arrays.stream(names).sorted().toList();
    }

    private static final java.util.Set<String> fonts = new java.util.HashSet<>();

    /** Makes the project's fonts (Assets/Fonts/*.ttf, .otf) usable by family name in fontFamily; again on a live reload for new ones. */
    private static void loadFonts() {
        for (var name : files("Assets/Fonts", ".ttf", ".otf"))
            if (fonts.add(name) && url("Assets/Fonts/" + name) instanceof URL font) javafx.scene.text.Font.loadFont(font.toExternalForm(), 12);
    }

    private static String read(String path) {
        var url = url(path);
        if (url == null) return "";
        try (var in = url.openStream()) { return new String(in.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8); }
        catch (IOException e) { return ""; }
    }

    /** ponytail: a flat "key": "value" lookup; faro.json is Faro-written and simple. Use a JSON library if it grows. */
    /** faro.json "tokens": {"space.m": 16, "color.primary": "#6750A4", "text.title.fontSize": 22, …} (flat names to numbers or strings). */
    static Map<String, String> tokens(String json) {
        var tokens = new HashMap<String, String>();
        var block = Pattern.compile("\"tokens\"\\s*:\\s*\\{([^}]*)\\}").matcher(json);
        if (block.find()) {
            // numbers, and strings (colors, font names with spaces: "Noto Sans JP")
            var pair = Pattern.compile("\"([^\"]+)\"\\s*:\\s*(?:\"([^\"]*)\"|([^,\\s}]+))").matcher(block.group(1));
            while (pair.find()) tokens.put(pair.group(1), pair.group(2) != null ? pair.group(2) : pair.group(3));
        }
        return tokens;
    }

    private static String value(String json, String key, String fallback) {
        var m = Pattern.compile("\"" + key + "\"\\s*:\\s*\"([^\"]*)\"").matcher(json);
        return m.find() ? m.group(1) : fallback;
    }
}

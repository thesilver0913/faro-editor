package faro.runtime;

import java.io.File;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.function.Function;
import javafx.geometry.HPos;
import javafx.geometry.Insets;
import javafx.geometry.Orientation;
import javafx.geometry.Pos;
import javafx.geometry.VPos;
import javafx.scene.Node;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.control.TextField;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.layout.ColumnConstraints;
import javafx.scene.layout.FlowPane;
import javafx.scene.layout.GridPane;
import javafx.scene.layout.HBox;
import javafx.scene.layout.Pane;
import javafx.scene.layout.Priority;
import javafx.scene.layout.Region;
import javafx.scene.layout.RowConstraints;
import javafx.scene.layout.StackPane;
import javafx.scene.layout.VBox;
import org.w3c.dom.Element;

/**
 * Turns a UIGraph &lt;Node&gt; tree (spec §5) into JavaFX nodes: the JavaFX twin of Faro.Runtime.UiBuilder (C#),
 * reading the same XML. Every built node is registered in byId; nodes inside an instance as "instanceId/innerId".
 */
public final class UiBuilder {
    private UiBuilder() { }

    /** Builds a Script node's class by name (the app resolves it in the user's classes). */
    public static Function<String, Node> scriptFactory;

    /** Project file → URL, set by the app (the project folder, or the resources bundled in an Android APK). */
    public static Function<String, java.net.URL> files;

    public static Node build(Element node, Map<String, Node> byId, File root, String prefix) {
        var type = node.getAttribute("type");
        Node control = switch (type) {
            case "Container.Stack", "Container.Wrap", "Container.Grid", "Container.Overlay" -> container(node, type, byId, root, prefix);
            case "Instance" -> instance(node, byId, root, prefix);
            case "Control.Script" -> script(node.getAttribute("class"));
            case "Control.Button" -> new Button(text(node, "Text"));
            case "Control.TextInput" -> {
                var field = new TextField(text(node, "Text"));
                field.setPromptText(prop(node, "Placeholder"));
                yield field;
            }
            case "Control.Text" -> {
                var label = new Label(text(node, "Text"));
                label.setWrapText(true);
                yield label;
            }
            case "Control.Image" -> image(root, prop(node, "Source"));
            default -> {
                var label = new Label("[unknown type: " + type + "]");
                label.setStyle("-fx-text-fill: red;");
                yield label;
            }
        };
        if (control instanceof Region region) {
            size(node, "width", region::setMinWidth, region::setPrefWidth, region::setMaxWidth);
            size(node, "height", region::setMinHeight, region::setPrefHeight, region::setMaxHeight);
            if (number(node, "minWidth") instanceof Double v) region.setMinWidth(v);
            if (number(node, "maxWidth") instanceof Double v) region.setMaxWidth(v);
            if (number(node, "minHeight") instanceof Double v) region.setMinHeight(v);
            if (number(node, "maxHeight") instanceof Double v) region.setMaxHeight(v);
        } else if (control instanceof ImageView view && "Fixed".equals(sizing(node, "width")) && number(node, "width") instanceof Double w) {
            view.setFitWidth(w);
        }
        control.managedProperty().bind(control.visibleProperty()); // hidden nodes leave the layout, as in Avalonia
        // Design-language options ("m3.variant"="Tonal") become style classes ("m3-variant-tonal") that only that language styles.
        var attributes = node.getAttributes();
        for (var i = 0; i < attributes.getLength(); i++) {
            var name = attributes.item(i).getNodeName();
            if (name.contains(".")) control.getStyleClass().add((name.replace('.', '-') + "-" + attributes.item(i).getNodeValue()).toLowerCase());
        }
        byId.put(prefix + node.getAttribute("id"), control);
        return control;
    }

    /** Fill / Hug / Fixed per axis: widthSizing/heightSizing, with "sizing" as shorthand for both. Default Hug. */
    public static String sizing(Element node, String axis) {
        var own = node.getAttribute(axis + "Sizing");
        if (!own.isEmpty()) return own;
        var both = node.getAttribute("sizing");
        return both.isEmpty() ? "Hug" : both;
    }

    private static void size(Element node, String axis, java.util.function.DoubleConsumer min, java.util.function.DoubleConsumer pref, java.util.function.DoubleConsumer max) {
        switch (sizing(node, axis)) {
            case "Fill" -> max.accept(Double.MAX_VALUE);
            case "Fixed" -> {
                if (number(node, axis) instanceof Double v) { min.accept(v); pref.accept(v); max.accept(v); }
            }
            default -> max.accept(Region.USE_PREF_SIZE);
        }
    }

    /**
     * The script's node inside a StackPane, so bindings see a Script node (Visible, Enabled) whatever it builds.
     * Unset or failing: a placeholder naming the class.
     */
    private static Node script(String name) {
        Node content;
        try {
            content = scriptFactory != null ? scriptFactory.apply(name) : placeholder("Script: " + name, "gray");
        } catch (RuntimeException e) {
            var cause = e.getCause() instanceof java.lang.reflect.InvocationTargetException t ? t.getCause() : e;
            content = placeholder("Script: " + name + "\n" + cause, "orangered");
        }
        return new StackPane(content);
    }

    private static Node placeholder(String text, String color) {
        var label = new Label(text);
        label.setWrapText(true);
        label.setStyle("-fx-text-fill: " + color + "; -fx-border-color: " + color + "; -fx-padding: 8; -fx-min-height: 40;");
        return label;
    }

    /** Every instance under a node, with the key prefix its inner nodes get in byId and its component. */
    public static List<String[]> instancePaths(Element node, String prefix) {
        var paths = new ArrayList<String[]>();
        if ("Instance".equals(node.getAttribute("type"))) {
            var inner = prefix + node.getAttribute("id") + "/";
            paths.add(new String[] { inner, node.getAttribute("component") });
            for (var snapshot : children(node, "Node")) paths.addAll(instancePaths(snapshot, inner));
            return paths;
        }
        for (var child : children(node, "Node")) paths.addAll(instancePaths(child, prefix));
        return paths;
    }

    public static String prop(Element node, String name) {
        for (var p : children(node, "Prop"))
            if (name.equals(p.getAttribute("name"))) return p.getAttribute("value");
        return null;
    }

    private static String text(Element node, String name) { return Objects.requireNonNullElse(prop(node, name), ""); }

    public static List<Element> children(Element node, String name) {
        var list = new ArrayList<Element>();
        for (var c = node.getFirstChild(); c != null; c = c.getNextSibling())
            if (c instanceof Element e && e.getTagName().equals(name)) list.add(e);
        return list;
    }

    /** faro.json "tokens" ({"space.m": 16}): a number attribute may name one as "$space.m" (set when the project loads). */
    public static Map<String, String> tokens = Map.of();

    private static String token(String value) { return value.startsWith("$") ? tokens.getOrDefault(value.substring(1), value) : value; }

    private static Double number(Element node, String name) {
        try { return node.hasAttribute(name) ? Double.valueOf(token(node.getAttribute(name).trim())) : null; }
        catch (NumberFormatException e) { return null; }
    }

    /**
     * Containers follow Figma Auto Layout, as in the C# runtime: Stack is a one-column (or one-row) GridPane where
     * Fill children grow on the main axis, "justify" packs or spreads the rest; Overlay anchors children to edges.
     * ponytail: Fill children share the free space equally ("weight" is ignored); percent tracks when it matters.
     */
    private static Node container(Element node, String type, Map<String, Node> byId, File root, String prefix) {
        var vertical = !"Horizontal".equals(node.getAttribute("direction"));
        var gap = number(node, "gap") instanceof Double g ? g : 0;
        var align = node.getAttribute("alignment");
        var kids = children(node, "Node");
        var built = kids.stream().map(k -> build(k, byId, root, prefix)).toList();
        for (var c : built) if (c instanceof RepeatHost list) list.orient(vertical, gap); // rows sit like siblings
        Pane pane;
        switch (type) {
            case "Container.Stack" -> pane = stack(node, kids, built, vertical, gap, align);
            case "Container.Overlay" -> {
                var overlay = new StackPane();
                for (var i = 0; i < kids.size(); i++) {
                    var n = kids.get(i);
                    var c = built.get(i);
                    var x = switch (n.getAttribute("anchorX")) { case "Center" -> HPos.CENTER; case "Right" -> HPos.RIGHT; default -> HPos.LEFT; };
                    var y = switch (n.getAttribute("anchorY")) { case "Center" -> VPos.CENTER; case "Bottom" -> VPos.BOTTOM; default -> VPos.TOP; };
                    StackPane.setAlignment(c, pos(y, x));
                    StackPane.setMargin(c, sides(n.getAttribute("margin")));
                    overlay.getChildren().add(c);
                }
                pane = overlay;
            }
            case "Container.Wrap" -> {
                var wrap = new FlowPane(vertical ? Orientation.VERTICAL : Orientation.HORIZONTAL, gap, gap);
                for (var i = 0; i < kids.size(); i++) {
                    FlowPane.setMargin(built.get(i), sides(kids.get(i).getAttribute("margin")));
                    wrap.getChildren().add(built.get(i));
                }
                pane = wrap;
            }
            default -> pane = grid(node, kids, built, gap, align);
        }
        pane.setPadding(sides(node.getAttribute("padding")));
        return pane;
    }

    private static Pane stack(Element node, List<Element> kids, List<Node> built, boolean vertical, double gap, String align) {
        var main = vertical ? "height" : "width";
        var cross = vertical ? "width" : "height";
        var justify = node.getAttribute("justify").isEmpty() ? "Start" : node.getAttribute("justify");
        var anyFill = kids.stream().anyMatch(k -> "Fill".equals(sizing(k, main)));
        var spread = "SpaceBetween".equals(justify) && !anyFill && kids.size() > 1;
        var grid = new GridPane();
        grid.setHgap(spread || vertical ? 0 : gap);
        grid.setVgap(spread || !vertical ? 0 : gap);
        if (vertical) grid.getColumnConstraints().add(grow(new ColumnConstraints()));
        else grid.getRowConstraints().add(grow(new RowConstraints()));
        var track = 0;
        for (var i = 0; i < kids.size(); i++) {
            var n = kids.get(i);
            var c = built.get(i);
            if (spread && i > 0) track(grid, vertical, true, null, track++); // the leftover space goes between the children
            track(grid, vertical, "Fill".equals(sizing(n, main)), c, track++);
            var self = n.getAttribute("alignSelf").isEmpty() ? align : n.getAttribute("alignSelf");
            var fill = "Fill".equals(sizing(n, cross));
            if (vertical) {
                GridPane.setFillWidth(c, fill);
                GridPane.setHalignment(c, switch (self) { case "Center" -> HPos.CENTER; case "End" -> HPos.RIGHT; default -> HPos.LEFT; });
            } else {
                GridPane.setFillHeight(c, fill);
                GridPane.setValignment(c, switch (self) { case "Center" -> VPos.CENTER; case "End" -> VPos.BOTTOM; default -> VPos.TOP; });
            }
            GridPane.setMargin(c, sides(n.getAttribute("margin")));
        }
        if (!anyFill && !spread) { // packed at Start/Center/End
            var at = switch (justify) { case "Center" -> 1; case "End" -> 2; default -> 0; };
            grid.setAlignment(vertical ? pos(new VPos[] { VPos.TOP, VPos.CENTER, VPos.BOTTOM }[at], HPos.LEFT)
                : pos(VPos.TOP, new HPos[] { HPos.LEFT, HPos.CENTER, HPos.RIGHT }[at]));
        }
        return grid;
    }

    private static void track(GridPane grid, boolean vertical, boolean grows, Node c, int index) {
        if (vertical) {
            var row = new RowConstraints();
            if (grows) row.setVgrow(Priority.ALWAYS);
            grid.getRowConstraints().add(row);
            if (c != null) grid.add(c, 0, index);
        } else {
            var column = new ColumnConstraints();
            if (grows) column.setHgrow(Priority.ALWAYS);
            grid.getColumnConstraints().add(column);
            if (c != null) grid.add(c, index, 0);
        }
    }

    private static ColumnConstraints grow(ColumnConstraints c) { c.setHgrow(Priority.ALWAYS); return c; }
    private static RowConstraints grow(RowConstraints r) { r.setVgrow(Priority.ALWAYS); return r; }

    /**
     * Grid with explicit tracks: columns="Auto, *, 2*, 120" ("3" = three equal columns), rows likewise.
     * A child sits at row / column spanning rowSpan / columnSpan; the others fill the free cells in reading order.
     */
    private static Pane grid(Element node, List<Element> kids, List<Node> built, double gap, String align) {
        var grid = new GridPane();
        grid.setHgap(gap);
        grid.setVgap(gap);
        var columns = tracks(node.getAttribute("columns"));
        if (columns == null) columns = List.of("*", "*");
        var starTotal = columns.stream().filter(t -> t.endsWith("*")).mapToDouble(UiBuilder::stars).sum();
        var onlyStars = columns.stream().allMatch(t -> t.endsWith("*"));
        for (var t : columns) {
            var c = new ColumnConstraints();
            if (t.endsWith("*")) {
                c.setHgrow(Priority.ALWAYS);
                if (onlyStars) c.setPercentWidth(stars(t) / starTotal * 100);
            } else if (!t.equals("Auto")) {
                var px = Double.parseDouble(t);
                c.setMinWidth(px); c.setPrefWidth(px); c.setMaxWidth(px);
            }
            grid.getColumnConstraints().add(c);
        }
        var count = columns.size();
        var taken = new HashSet<Long>();
        var explicit = new HashSet<Integer>();
        var maxRow = -1;
        for (var i = 0; i < kids.size(); i++) {
            var n = kids.get(i);
            if (!n.hasAttribute("row") && !n.hasAttribute("column")) continue;
            explicit.add(i);
            maxRow = Math.max(maxRow, place(grid, built.get(i), taken, count, integer(n, "row", 0), integer(n, "column", 0), Math.max(integer(n, "rowSpan", 1), 1), integer(n, "columnSpan", 1)));
        }
        var cell = 0;
        for (var i = 0; i < kids.size(); i++) {
            if (explicit.contains(i)) continue;
            while (taken.contains(key(cell / count, cell % count))) cell++;
            maxRow = Math.max(maxRow, place(grid, built.get(i), taken, count, cell / count, cell % count, 1, 1));
        }
        var rows = tracks(node.getAttribute("rows"));
        for (var r = 0; r <= Math.max(maxRow, rows == null ? -1 : rows.size() - 1); r++) {
            var row = new RowConstraints();
            var t = rows != null && r < rows.size() ? rows.get(r) : "Auto";
            if (t.endsWith("*")) row.setVgrow(Priority.ALWAYS);
            else if (!t.equals("Auto")) { var px = Double.parseDouble(t); row.setMinHeight(px); row.setPrefHeight(px); row.setMaxHeight(px); }
            grid.getRowConstraints().add(row);
        }
        for (var i = 0; i < kids.size(); i++) { // both axes inside the cell
            var n = kids.get(i);
            var c = built.get(i);
            var self = n.getAttribute("alignSelf").isEmpty() ? align : n.getAttribute("alignSelf");
            GridPane.setFillWidth(c, "Fill".equals(sizing(n, "width")));
            GridPane.setFillHeight(c, "Fill".equals(sizing(n, "height")));
            GridPane.setHalignment(c, switch (self) { case "Center" -> HPos.CENTER; case "End" -> HPos.RIGHT; default -> HPos.LEFT; });
            GridPane.setValignment(c, switch (self) { case "Center" -> VPos.CENTER; case "End" -> VPos.BOTTOM; default -> VPos.TOP; });
            GridPane.setMargin(c, sides(n.getAttribute("margin")));
        }
        return grid;
    }

    private static int place(GridPane grid, Node c, Set<Long> taken, int columns, int row, int column, int rowSpan, int columnSpan) {
        column = Math.min(column, columns - 1);
        columnSpan = Math.max(1, Math.min(columnSpan, columns - column));
        grid.add(c, column, row, columnSpan, rowSpan);
        for (var r = row; r < row + rowSpan; r++)
            for (var col = column; col < column + columnSpan; col++) taken.add(key(r, col));
        return row + rowSpan - 1;
    }

    private static long key(int row, int column) { return ((long) row << 32) | column; }

    private static int integer(Element n, String name, int fallback) {
        try { return n.hasAttribute(name) ? Math.max(Integer.parseInt(n.getAttribute(name).trim()), 0) : fallback; }
        catch (NumberFormatException e) { return fallback; }
    }

    private static double stars(String t) { return t.length() == 1 ? 1 : Double.parseDouble(t.substring(0, t.length() - 1)); }

    /** "Auto, *, 2*, 120px" → ["Auto", "*", "2*", "120"]; a lone number is a count ("3" → three "*"). Null when empty or invalid. */
    public static List<String> tracks(String spec) {
        if (spec == null || spec.isBlank()) return null;
        try {
            var count = Integer.parseInt(spec.trim());
            return count > 0 ? java.util.Collections.nCopies(count, "*") : null;
        } catch (NumberFormatException ignored) { }
        var list = new ArrayList<String>();
        for (var token : spec.split("[,\\s]+")) {
            if (token.isEmpty()) continue;
            if (token.equalsIgnoreCase("Auto")) list.add("Auto");
            else if (token.matches("\\d*(\\.\\d+)?\\*")) list.add(token);
            else if (token.matches("\\d+(\\.\\d+)?(px)?")) list.add(token.replace("px", ""));
            else return null;
        }
        return list;
    }

    /** "16", "8 16" (vertical horizontal) or "8 16 4 16" (top right bottom left), CSS order; commas work too. */
    public static Insets sides(String text) {
        var parts = text == null ? new String[0] : text.trim().split("[,\\s]+");
        var v = new double[parts.length];
        for (var i = 0; i < parts.length; i++) {
            try { v[i] = Double.parseDouble(token(parts[i])); } catch (NumberFormatException e) { v[i] = 0; }
        }
        return switch (v.length) {
            case 1 -> new Insets(v[0]);
            case 2 -> new Insets(v[0], v[1], v[0], v[1]);
            case 4 -> new Insets(v[0], v[1], v[2], v[3]);
            default -> Insets.EMPTY;
        };
    }

    private static Pos pos(VPos v, HPos h) {
        return v == VPos.CENTER && h == HPos.CENTER ? Pos.CENTER : Pos.valueOf((v == VPos.CENTER ? "CENTER" : v.name()) + "_" + h.name());
    }

    /** Instances render the master snapshot stored inside them, with their &lt;Override&gt;s applied. */
    private static Node instance(Element node, Map<String, Node> byId, File root, String prefix) {
        var copy = copy(node, byId, root, prefix);
        return "true".equals(node.getAttribute("repeatable")) ? new RepeatHost(node, copy) : copy;
    }

    /** One copy of an instance (a list item's row, for a repeatable one). */
    public static Node copy(Element node, Map<String, Node> byId, File root, String prefix) {
        var snapshots = children(node, "Node");
        if (snapshots.isEmpty()) return placeholder("[not synced: " + node.getAttribute("component") + "]", "orangered");
        var copy = (Element) snapshots.get(0).cloneNode(true);
        for (var o : children(node, "Override")) {
            for (var p : children(copy, "Prop"))
                if (p.getAttribute("name").equals(o.getAttribute("prop"))) copy.removeChild(p);
            var p = copy.getOwnerDocument().createElement("Prop");
            p.setAttribute("name", o.getAttribute("prop"));
            p.setAttribute("value", o.getAttribute("value"));
            copy.appendChild(p);
        }
        // The instance's own design options ("m3.variant") win over its master's (or variant's): one style class each.
        var attributes = node.getAttributes();
        for (var i = 0; i < attributes.getLength(); i++)
            if (attributes.item(i).getNodeName().contains(".")) copy.setAttribute(attributes.item(i).getNodeName(), attributes.item(i).getNodeValue());
        var row = build(copy, byId, root, prefix + node.getAttribute("id") + "/");
        if ("true".equals(node.getAttribute("repeatable")) && row instanceof Region r) r.setMaxSize(Double.MAX_VALUE, Double.MAX_VALUE); // rows stretch across the list
        return row;
    }

    private static Node image(File root, String path) {
        var view = new ImageView();
        view.setPreserveRatio(true);
        var url = path == null ? null : files.apply(path);
        if (url != null) view.setImage(new Image(url.toExternalForm()));
        return view;
    }

    /**
     * A repeatable instance (a list): shows its snapshot until its Items are bound, then one copy per item
     * (FaroApp). The canvas shows mock rows instead, so this is the app's side of repeatable.
     */
    public static final class RepeatHost extends StackPane {
        public final Element node;
        private Pane rows = new VBox();

        RepeatHost(Element node, Node first) {
            this.node = node;
            setAlignment(Pos.TOP_LEFT);
            rows.getChildren().add(first);
            getChildren().add(rows);
        }

        void orient(boolean vertical, double gap) {
            var kids = rows.getChildren().toArray(Node[]::new);
            rows = vertical ? new VBox(gap, kids) : new HBox(gap, kids);
            getChildren().setAll(rows);
        }

        public List<Node> rows() { return rows.getChildren(); }
    }
}

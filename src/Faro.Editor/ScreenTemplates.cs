using System.Xml.Linq;

namespace Faro.Editor;

/// <summary>
/// Starting layouts for a new screen (layout only: no bindings or code), with their text in the editor's language.
/// Only parts every design language draws; icons from the bundled set.
/// </summary>
public static class ScreenTemplates
{
    public static readonly string[] Names = ["Blank", "Login", "List", "Detail", "Settings", "Form", "Empty state"];

    static XElement N(string id, string type, params object[] content) => new("Node", new XAttribute("id", id), new XAttribute("type", type), content);
    static XAttribute A(string name, string value) => new(name, value);
    static XElement P(string name, string value) => new("Prop", new XAttribute("name", name), new XAttribute("value", value));
    static XElement Text(string id, string text, string? size = null, bool bold = false, params object[] more) =>
        N(id, "Control.Text", size is null ? null! : A("fontSize", size), bold ? A("fontWeight", "Bold") : null!, more, P("Text", L.T(text)));
    static XElement Icon(string id, string icon, string size, params object[] more) =>
        N(id, "Control.Icon", A("widthSizing", "Fixed"), A("width", size), A("heightSizing", "Fixed"), A("height", size), more, P("Icon", icon));
    static XElement Button(string id, string text, params object[] more) => N(id, "Control.Button", more, P("Text", L.T(text)));
    static XElement Input(string id, string placeholder, params object[] more) => N(id, "Control.TextInput", A("widthSizing", "Fill"), more, P("Placeholder", L.T(placeholder)));
    static XElement Row(string id, params object[] content) => N(id, "Container.Stack", A("direction", "Horizontal"), A("gap", "12"), A("alignment", "Center"), A("widthSizing", "Fill"), content);

    /// <summary>The screen's root node for a template (Blank: an empty column).</summary>
    public static XElement Root(string template)
    {
        var root = N("root", "Container.Stack", A("direction", "Vertical"), A("gap", "12"), A("padding", "16"));
        switch (template)
        {
            case "Login":
                root.SetAttributeValue("justify", "Center");
                root.SetAttributeValue("padding", "32");
                root.Add(Icon("logo", "account_circle", "64", A("alignSelf", "Center")), Text("title", "Sign in", "24", true, A("alignSelf", "Center")),
                    Input("email", "Email"), Input("password", "Password", P("Password", "true")),
                    Button("signIn", "Sign in", A("widthSizing", "Fill")), Button("forgot", "Forgot password?", A("alignSelf", "Center"), A("look.variant", "Text"), A("m3.variant", "Text")));
                break;
            case "List":
                root.Add(Row("header", Text("title", "Items", "22", true), N("space", "Control.Spacer"), Button("add", "Add")));
                for (var i = 1; i <= 3; i++)
                    root.Add(Row($"row{i}", A("padding", "8 0"), Icon($"icon{i}", "star_border", "24"),
                        N($"texts{i}", "Container.Stack", A("direction", "Vertical"), A("gap", "2"), A("widthSizing", "Fill"),
                            Text($"name{i}", L.F("Item {0}", i), bold: true), Text($"detail{i}", "Details", "12")),
                        Icon($"open{i}", "chevron_right", "24")),
                        N($"line{i}", "Control.Divider"));
                break;
            case "Detail":
                root.Add(Row("bar", Button("back", "Back", A("look.variant", "Text"), A("m3.variant", "Text")), Text("title", "Details", "18", true)),
                    Text("heading", "Title", "24", true), Text("body", "A description of the item goes here."), N("line", "Control.Divider"),
                    Row("price", Text("priceLabel", "Price"), N("space", "Control.Spacer"), Text("priceValue", "¥0", bold: true)),
                    Row("date", Text("dateLabel", "Date"), N("space2", "Control.Spacer"), Text("dateValue", "2026-01-01")),
                    N("fill", "Control.Spacer"), Button("action", "Edit", A("widthSizing", "Fill")));
                root.SetAttributeValue("heightSizing", "Fill");
                break;
            case "Settings":
                root.Add(Text("title", "Settings", "22", true), Text("general", "General", "12", true),
                    N("notifications", "Control.Switch", A("widthSizing", "Fill"), P("Text", L.T("Notifications"))),
                    N("dark", "Control.Switch", A("widthSizing", "Fill"), P("Text", L.T("Dark mode"))), N("line", "Control.Divider"),
                    Text("languageLabel", "Language", "12", true), N("language", "Control.Select", A("widthSizing", "Fill"), P("Options", "English, 日本語"), P("Selected", "English")),
                    N("line2", "Control.Divider"), Button("signOut", "Sign out", A("look.variant", "Text"), A("m3.variant", "Text")));
                break;
            case "Form":
                root.Add(Text("title", "New item", "22", true), Input("name", "Name"),
                    N("date", "Control.DateInput", A("widthSizing", "Fill"), P("Placeholder", L.T("Date"))),
                    N("amount", "Control.NumberInput", A("widthSizing", "Fill"), P("Placeholder", L.T("Amount"))),
                    N("category", "Control.Select", A("widthSizing", "Fill"), P("Options", L.T("Food, Transport, Other")), P("Selected", L.T("Food, Transport, Other").Split(',')[0].Trim())),
                    Input("note", "Note"),
                    Row("actions", A("justify", "End"), Button("cancel", "Cancel", A("look.variant", "Text"), A("m3.variant", "Text")), Button("save", "Save")));
                break;
            case "Empty state":
                root.SetAttributeValue("justify", "Center");
                root.SetAttributeValue("padding", "32");
                root.Add(Icon("picture", "bookmark_border", "72", A("alignSelf", "Center")), Text("title", "Nothing here yet", "20", true, A("alignSelf", "Center")),
                    Text("hint", "Add your first item to get started.", more: A("alignSelf", "Center")), Button("add", "Add item", A("alignSelf", "Center")));
                break;
        }
        return root;
    }
}

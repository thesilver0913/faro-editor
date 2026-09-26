using Faro.Runtime;

namespace MyApp.Models;

public class UserProfile : FaroObject
{
    public string Name { get; set { Set(ref field, value); Set(ref greeting, $"こんにちは、{value}さん", nameof(Greeting)); } } = "";

    string greeting = "";
    public string Greeting => greeting;
}

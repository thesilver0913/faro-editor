using Faro.Runtime;

namespace MyApp.Services;

[FaroLifetime(Lifetime.Singleton, Persistent = true)]
public class OrderService : FaroObject
{
    public int SubmitCount { get; set { Set(ref field, value); Set(ref summary, $"送信回数: {value}", nameof(Summary)); } }

    string summary = "送信回数: 0";
    public string Summary => summary;

    public void Submit() => SubmitCount++;
}

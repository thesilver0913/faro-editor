using System.Collections.ObjectModel;
using Faro.Runtime;
using MyApp.Models;

namespace MyApp.Services;

[FaroLifetime(Lifetime.Singleton, Persistent = true)]
public class OrderService : FaroObject
{
    public int SubmitCount { get; set { Set(ref field, value); Set(ref summary, $"送信回数: {value}", nameof(Summary)); } }

    string summary = "送信回数: 0";
    public string Summary => summary;

    public ObservableCollection<Order> Orders { get; set; } = [new() { Name = "りんご", Price = 120 }];

    public void Submit() => Orders.Add(new() { Name = $"注文 {++SubmitCount}", Price = 100 * SubmitCount });
}

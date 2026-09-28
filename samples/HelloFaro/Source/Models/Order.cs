namespace MyApp.Models;

/// <summary>One row of the order list (orderList's Items): its members bind to the row's controls.</summary>
public class Order
{
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public string PriceText => $"¥{Price}";
}

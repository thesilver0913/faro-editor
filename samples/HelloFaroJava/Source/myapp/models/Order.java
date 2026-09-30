package myapp.models;

/** One row of the order list (orderList's Items): its properties bind to the row's controls. */
public class Order {
    private final String name;
    private final int price;

    /** An empty order, as the C# sample's: Detail opened without a row shows blanks rather than an error. */
    public Order() { this("", 0); }

    public Order(String name, int price) {
        this.name = name;
        this.price = price;
    }

    public String getName() { return name; }

    public int getPrice() { return price; }
}

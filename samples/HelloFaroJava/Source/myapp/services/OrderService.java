package myapp.services;

import faro.runtime.FaroApp;
import faro.runtime.FaroLifetime;
import faro.runtime.FaroObject;
import faro.runtime.Lifetime;
import java.util.ArrayList;
import java.util.List;
import myapp.models.Order;

@FaroLifetime(value = Lifetime.SINGLETON, persistent = true)
public class OrderService extends FaroObject {
    private int submitCount;
    private final List<Order> orders = new ArrayList<>(List.of(new Order("りんご", 120)));

    public int getSubmitCount() { return submitCount; }

    public void setSubmitCount(int value) {
        submitCount = value;
        changed("submitCount", "summary");
    }

    public String getSummary() { return "送信回数: " + submitCount; }

    public List<Order> getOrders() { return orders; }

    /** A tapped row (its item comes in): the detail screen shows that order. */
    public void open(Order order) { FaroApp.navigate("Detail", order); }

    public void submit() {
        setSubmitCount(submitCount + 1);
        orders.add(new Order("注文 " + submitCount, 100 * submitCount));
        changed("orders"); // redraws the list bound to orders
    }
}

package myapp.services;

import faro.runtime.FaroLifetime;
import faro.runtime.FaroObject;
import faro.runtime.Lifetime;

@FaroLifetime(value = Lifetime.SINGLETON, persistent = true)
public class OrderService extends FaroObject {
    private int submitCount;

    public int getSubmitCount() { return submitCount; }

    public void setSubmitCount(int value) {
        submitCount = value;
        changed("submitCount", "summary");
    }

    public String getSummary() { return "送信回数: " + submitCount; }

    public void submit() { setSubmitCount(submitCount + 1); }
}

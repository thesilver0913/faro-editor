package myapp.models;

import faro.runtime.FaroObject;

public class UserProfile extends FaroObject {
    private String name = "";

    public String getName() { return name; }

    public void setName(String value) {
        name = value;
        changed("name", "greeting");
    }

    public String getGreeting() { return "こんにちは、" + name + "さん"; }
}

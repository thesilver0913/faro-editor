package faro.runtime;

import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.function.Consumer;

/** Change-notification base for user classes, so bindings see code-side updates (spec §6). */
public abstract class FaroObject {
    private final List<Consumer<String>> listeners = new CopyOnWriteArrayList<>();

    public void addChangeListener(Consumer<String> listener) { listeners.add(listener); }

    public void removeChangeListener(Consumer<String> listener) { listeners.remove(listener); }

    /** Tells bindings these properties changed; they read the getters again. */
    protected void changed(String... properties) {
        for (var property : properties)
            for (var listener : listeners) listener.accept(property);
    }
}

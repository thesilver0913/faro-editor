package faro.runtime;

/**
 * Base class for a Script node's class (type="Control.Script" class="pkg.Class"): look, controls and behaviour
 * all written in code. build() returns the JavaFX node shown in the node's place; lifetimes apply as usual.
 */
public abstract class FaroScript extends FaroObject {
    public abstract javafx.scene.Node build();
}

package myapp.views;

import faro.runtime.FaroScript;
import javafx.scene.Node;
import javafx.scene.control.Button;
import javafx.scene.control.Label;
import javafx.scene.layout.VBox;

/** A Script node's class: its look, button and behaviour are all written in code. */
public class Stamp extends FaroScript {
    private int stamps;

    @Override
    public Node build() {
        var label = new Label("スタンプ: 0");
        var button = new Button("押す");
        button.setOnAction(e -> label.setText("スタンプ: " + ++stamps));
        var box = new VBox(8, label, button);
        box.setStyle("-fx-background-color: aliceblue; -fx-background-radius: 8; -fx-padding: 12;");
        return box;
    }
}

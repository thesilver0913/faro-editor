package faro.runtime;

import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/** Lifetime of a user class; persistent keeps its bean properties across app runs (saved when released). */
@Retention(RetentionPolicy.RUNTIME)
@Target(ElementType.TYPE)
public @interface FaroLifetime {
    Lifetime value();
    boolean persistent() default false;
}

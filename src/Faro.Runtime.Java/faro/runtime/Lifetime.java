package faro.runtime;

/** Instance lifetime of a user class (spec §6.5). Classes without {@link FaroLifetime} are SCREEN_SCOPED. */
public enum Lifetime { SINGLETON, SCREEN_SCOPED, TRANSIENT }

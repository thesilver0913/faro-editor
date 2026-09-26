// Spec 11.5: Is a MethodInfo cached before a hot reload still valid (and calling the new body) after it?
var cached = typeof(Target).GetMethod(nameof(Target.Hello))!;
var instance = new Target();
for (var i = 0; i < 120; i++)
{
    Console.WriteLine($"cached={cached.Invoke(instance, null)} fresh={typeof(Target).GetMethod("Hello")!.Invoke(instance, null)} sameHandle={cached == typeof(Target).GetMethod("Hello")}");
    Thread.Sleep(500);
}

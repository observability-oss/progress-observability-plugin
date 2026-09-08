# C# field notes for a Python programmer

A simplified companion to the Copilot notes and agent walkthrough. .NET 10; 8 September 2026.

[Interactive learning game](./learning-game.html) · [Formatted, printable cheat sheet](./cheat-sheet.html)

## The distinctions that do most of the work

- **Language / platform:** C# is what you write. .NET runs it and supplies libraries. The SDK builds it. ASP.NET Core handles web apps.
- **Type / object:** `Jar` describes a kind of object. `new Jar(2)` makes one. `jar` holds a reference to that instance.
- **Function / method:** A method is a function declared on a type. Keep plain calculations small; introduce an object when shared state needs operations.
- **Parameter / inheritance:** `IText inner` receives an object. `: IText` implements a contract. `: Parent` derives from a class.
- **Query / results:** `Where(...)` usually describes work. `ToList()` runs it now and stores the results. The query variable is not a cache.
- **Task / result:** `Task<int>` represents eventual completion with an integer. `await` obtains the integer or propagates failure/cancellation.
- **Async / threads:** Calling a C# async Task method starts it immediately. An incomplete await can yield; it does not require a new thread.
- **Absent / empty:** `null` means absent. `""` is present empty text. `??` replaces only null; `!` provides no runtime protection.
- **Copy / shared data:** Class assignment copies a reference. Struct assignment copies its value. A record’s `with` copy still shares nested references.
- **Readonly / immutable:** `readonly`, `init`, and read-only interfaces limit specific operations. They do not automatically freeze nested objects.
- **Disposal / garbage collection:** `using var` releases a disposable resource at scope exit. Garbage collection reclaims managed memory on its own schedule.
- **Typed / validated:** A typed object can still contain invalid values. Validate external input before relying on it; successful parsing is only one step.

## Read the symbols

| C# | Meaning | Python connection / caution |
|---|---|---|
| `var count = 3;` | Infer int once; the variable cannot later hold text. | `count = 3` permits later rebinding to another type. |
| `foreach (var x in xs)` | Visit each element of a sequence. | `for x in xs:` |
| `x => x * 2` | Lambda; `=>` also appears in short method/property bodies. | `lambda x: x * 2` |
| `Func<int, int>` | A callable taking int and returning int; last type is the result. | A typed callable, not the integer result. |
| `string?` / `long?` | Nullable reference annotation / nullable value-type wrapper. | Both allow absence conceptually, but C# represents them differently. |
| `x?.Name ?? "Guest"` | Read safely; replace null with Guest. | Use explicit `is not None`; Python `or` also replaces empty/zero. |
| `x is "red" or "blue"` | C# pattern matching; this is not a regular expression. | `x in ("red", "blue")` for this simple case. |
| `Where` / `Any` / `ToList` | Filter a query / answer a bool now / materialize results. | Generator/filter / `any` / `list` are useful analogies. |
| `async Task<T>` | An async method eventually supplies T; `Task` alone has no result. | Calling Python `async def` normally creates a coroutine; execution differs. |
| `using var item = ...;` | Dispose item when the enclosing scope exits. | `with` is the closest resource-lifetime comparison. |
| `await foreach` | Consume an asynchronous sequence one element at a time. | `async for` |
| `[Description("...")]` | Attach metadata that a library may inspect. | Not Python decorator syntax that necessarily replaces/wraps a function. |

Examples assume ordinary SDK implicit imports unless a framework context is stated.

## 1. Read your first C# program

- C# is the language. .NET supplies the runtime and libraries; the SDK supplies development tools; ASP.NET Core builds web apps.
- A .csproj sets the target framework and dependencies. dotnet build compiles; dotnet run normally builds, then starts the app.
- Main is the entry point. Top-level statements generate it; .NET 10 also supports file-based apps without a .csproj.
- var infers one compile-time type. int, bool and string mean integer, Boolean and text; they are not interchangeable.
- Arrays hold typed elements. foreach visits them. && requires both conditions; a single-statement body can omit braces.

```csharp
var minimum = 2;                 // inferred int, not dynamic
int[] apples = [1, 3, 2];       // an array of integers
foreach (var count in apples)
{
    if (count >= minimum && count < 3)
        Console.WriteLine(count);
}
```

**Watch:** var does not mean Python-style rebinding to any type. Top-level code is still compiled C#.

<details><summary>Python connection</summary>

```python
minimum = 2
apples = [1, 3, 2]
for count in apples:
    if count >= minimum and count < 3:
        print(count)
```

</details>

<details><summary>More symbols, briefly</summary>

- **A tiny project file:** <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project> defines an executable project. Web projects use Microsoft.NET.Sdk.Web.
- **C# 14 and .NET 10:** These are the lesson targets. C# is versioned separately from .NET, and many demonstrated features existed before C# 14.
- **Entry-point shape:** static void Main() is an explicit entry method inside a type. Alternatively, top-level statements appear before type declarations; only one project source file may contain top-level statements.
- **File-based app:** With .NET 10, dotnet run --file hello.cs builds and runs a standalone C# source file. The generated agents use projects; some development helpers can be file-based apps.
- **Namespace:** namespace Pantry; groups type names logically. A namespace need not equal a folder, though matching them often helps readers.
- **using as an import:** using System; lets you write Console instead of System.Console. It neither copies source nor installs a package. using for disposal is a separate syntax.
- **Explicit typing and literals:** int count = 2; bool ready = true; string name = "Ada"; use explicit types. C# uses semicolons and braces; // starts a line comment.
- **while, continue and break:** while (count > 0) repeats while a condition stays true. continue skips to the next loop iteration; break exits the nearest loop. Make progress toward a stopping condition.

</details>

In the agents: Every generated app starts in Program.cs; its .csproj selects .NET and packages before request handling begins.

## 2. Functions, methods and little helpers

- Read int Double(int number) as: returns int, named Double, accepts one int. void means no return value.
- A static method belongs to its type; an instance method uses an object. A local function belongs inside another body.
- Overloads share a name but differ in parameters. An override replaces an inherited virtual or abstract implementation.
- number => number + extra is a lambda. Func<int, int> describes input and output types; captures can observe later changes.
- out supplies an output; ref accesses an initialized caller variable; params accepts several arguments. Recursion needs a stopping case.

```csharp
var extra = 2;
Func<int, int> addExtra = number => number + extra;
Console.WriteLine(addExtra(3));
extra = 4;
Console.WriteLine(addExtra(3));
static int Double(int number) => number * 2;
Console.WriteLine(Double(3));
```

**Watch:** The arrow can introduce a lambda or an expression-bodied member. Neither makes the computation asynchronous.

<details><summary>Python connection</summary>

```python
extra = 2
add_extra = lambda number: number + extra
print(add_extra(3))
extra = 4
print(add_extra(3))
def double(number):
    return number * 2
print(double(3))
```

</details>

<details><summary>More symbols, briefly</summary>

- **Return versus output:** return value; sends a value back to the caller. Console.WriteLine(value) displays it. void means the function has no return value.
- **A method in a class:** public static int Double(int number) { return number * 2; } is called as Helpers.Double(3) when declared in Helpers. Its expression-bodied equivalent ends with => number * 2;.
- **Instance method:** jar.Add(3) acts on jar. Inside an ordinary instance method, this denotes that current object; a static method has no this.
- **Local function:** A helper declared inside a method or top-level body can use surrounding locals. Add static to forbid capturing surrounding instance or local state.
- **Lambda and delegate:** Func<int, int> accepts an int and returns an int. Action<int> accepts an int and returns void. A lambda supplies a callable value; => also appears in expression-bodied members.
- **out:** int.TryParse(text, out var number) assigns number and returns a success bool. The caller does not have to initialize an out argument.
- **ref:** void Increment(ref int value) { value++; } can change a caller variable. Initialize int n = 2; then call Increment(ref n). Ordinary reference-type arguments already pass an object reference by value; ref is not needed to mutate that object.
- **params:** static int Count(params int[] numbers) => numbers.Length; accepts Count(1, 2, 3) or Count(new[] { 1, 2, 3 }). params makes the last parameter accept a variable number of arguments.
- **Recursion:** A recursive helper calls itself on a smaller subproblem. A stopping case prevents endless calls; recursion is useful for nested structures, not a requirement for ordinary loops.
- **nameof:** nameof(Double) produces the string "Double" at compile time when that symbol is in scope. It names the symbol without calling the function or reading its value.

</details>

In the agents: Tool handlers are methods; routing and LINQ use lambdas. Small helpers can stay functions without new class hierarchies.

## 3. Missing values without guessing

- null means absent, not empty text or zero. string? marks a nullable reference; int? can hold an integer or null.
- <Nullable>enable</Nullable> enables reference annotations and compiler analysis. An annotation does not validate runtime input.
- value?.Member accesses only when non-null; value ?? fallback uses the fallback only when value is null.
- value! suppresses a compiler warning; it performs no runtime check. Prefer proving a value exists before using it.
- is matches patterns: is null, is not null, or is "red" or "blue". && evaluates the right side only if needed.

```csharp
string? nickname = null;
string display = nickname ?? "Guest";
Console.WriteLine(display);
Console.WriteLine(nickname?.Length ?? 0);
nickname = "Ada";
if (nickname is not null && nickname.Length > 2)
    Console.WriteLine("long enough");
```

**Watch:** name!.Length still throws if name is null. The exclamation mark changes compiler analysis, not the object.

<details><summary>Python connection</summary>

```python
nickname = None
display = nickname if nickname is not None else "Guest"
print(display)
print(len(nickname) if nickname is not None else 0)
nickname = "Ada"
if nickname is not None and len(nickname) > 2:
    print("long enough")
```

</details>

<details><summary>More symbols, briefly</summary>

- **Nullable reference versus nullable value:** string? and string use the same runtime reference type; ? enables compiler analysis. int? is Nullable<int>, a value type that represents an integer or no value.
- **Conditional expression:** condition ? whenTrue : whenFalse selects one result. It is different from the nullable annotation in string? and the null-conditional operator ?. .
- **Value patterns:** color is "red" or "blue" accepts either text value. count is >= 1 and <= 3 combines relational patterns; or/and here are pattern combinators, not ||/&& operators.
- **Pattern captures:** if (value is string text) proves value is a string and introduces text for the matching path. A property pattern can inspect shape: value is { Length: > 2 }.
- **Null-safe length test:** name is not null && name.Length > 3 checks existence before accessing Length. Merely writing name! does not check anything at runtime.
- **Assign a null fallback:** name ??= "Guest" assigns "Guest" only if name is currently null. name ?? "Guest" merely produces a fallback value without assigning it to name.

</details>

In the agents: Optional configuration and request properties need explicit defaults or validation before handlers dereference them.

## 4. An object is data with useful operations

- A class is a type; new Jar(2) creates an instance. A constructor initializes each new object.
- A field stores state. A property provides access; get reads, set assigns, and init restricts assignment to initialization.
- Class primary-constructor parameters are inputs, not automatic public properties. Declare properties explicitly when callers need them.
- public exposes; private confines to the type; internal confines to the assembly. static belongs to the type; sealed forbids subclassing.
- readonly fields cannot be reassigned after their allowed initialization. Referenced objects may still mutate. Object initializers run after construction.

```csharp
var jar = new Jar(2) { Label = "Rice" };
jar.Add(3);
Console.WriteLine($"{jar.Label}: {jar.Count}");
sealed class Jar(int start)
{
    private int count = start;
    public string Label { get; init; } = "Plain";
    public int Count => count;
    public void Add(int amount) => count += amount;
}
```

**Watch:** A primary constructor is shorter syntax for accepting construction inputs, not a second object or an automatic public API.

<details><summary>Python connection</summary>

```python
class Jar:
    def __init__(self, start):
        self._count = start
        self.label = "Plain"
    @property
    def count(self):
        return self._count
    def add(self, amount):
        self._count += amount
jar = Jar(2)
jar.label = "Rice"
jar.add(3)
print(f"{jar.label}: {jar.count}")
```

</details>

<details><summary>More symbols, briefly</summary>

- **Ordinary constructor:** class Jar { private int count; public Jar(int start) { count = start; } } spells out construction explicitly. Constructors use the type name and have no declared return type.
- **Primary-constructor equivalent:** class Jar(int start) { private int count = start; } accepts the same input more concisely. start is a parameter, not a public property; it can be used within the type body.
- **Field versus property:** private int count; is storage. public int Count => count; is a getter exposing that storage. public string Label { get; set; } is an auto-property with compiler-provided storage.
- **Initializer timing:** new Jar(2) { Label = "Rice" } constructs the object first, then assigns Label during initialization. required demands initialization by the caller; it does not validate the supplied value.
- **readonly is shallow:** A readonly List<int> field cannot normally be reassigned outside field initialization or its declaring constructor, but its existing list can still Add elements.
- **Shared static state:** A static field belongs to the type rather than one instance. Every instance can observe shared state through it; do not use it merely to avoid constructing an object.
- **Accessibility and assembly:** An assembly is a compiled .dll or .exe unit. internal restricts ordinary access to that assembly; private restricts access to the declaring type, not the entire source file.
- **const versus readonly:** const int Limit = 5; declares a compile-time constant. A readonly field can instead be initialized at runtime during construction; neither keyword makes a referenced object deeply immutable.
- **protected:** A protected member is accessible within its declaring class and derived classes, subject to C# receiver rules. It is useful for base-class helpers; it is not publicly callable by any object.

</details>

In the agents: Stores and tool handlers keep state in fields and expose deliberate methods/properties; their constructors receive dependencies.

## 5. Data shapes and copies

- Class variables hold references; assigning one copies the reference. Struct assignment copies its value, including any references inside it.
- record means record class unless struct is stated. Records generate value-based equality using their members’ equality rules.
- Positional record-class parameters generate public init properties and deconstruction. Ordinary class primary parameters do not.
- with copies a record and changes selected members. Nested reference objects remain shared: the copy is shallow.
- Tuples group a few values: (string Name, int Count). Deconstruction, var (name, count), puts the components into local variables.

```csharp
var first = new Label("Blue", 2);
var same = new Label("Blue", 2);
var changed = first with { Size = 3 };
Console.WriteLine(first == same);
Console.WriteLine($"{first.Size}/{changed.Size}");
var (color, size) = changed;
Console.WriteLine($"{color}: {size}");
record Label(string Color, int Size);
```

**Watch:** Records are not deeply immutable. A record containing List<string> can share a mutable list, and list equality is not element equality.

<details><summary>Python connection</summary>

```python
from dataclasses import dataclass, replace
@dataclass(frozen=True)
class Label:
    color: str
    size: int
first = Label("Blue", 2)
same = Label("Blue", 2)
changed = replace(first, size=3)
print(first == same)
print(f"{first.size}/{changed.size}")
```

</details>

<details><summary>More symbols, briefly</summary>

- **record class versus record struct:** record Label(string Color, int Size) is a reference type with generated init properties. record struct Label(...) is a value type whose generated positional properties are normally mutable; readonly record struct makes them init-only.
- **Nested collection equality:** Two records containing separate List<int> objects with equal elements are not automatically equal: each list normally uses reference equality. Records use the members’ own equality rules.
- **Tuple deconstruction:** var snack = (Name: "Pear", Count: 2); var (name, count) = snack; creates local variables. Tuple labels aid readability; a tuple is not an interchangeable instance of a named record.
- **A with copy is shallow:** A copied record gets copied member values. Reference-valued members still point at the original nested objects unless explicitly replaced with fresh copies.
- **Anonymous object:** var snack = new { Name = "Pear", Count = 2 }; creates a compiler-generated type with read-only properties. new Jar { Label = "Rice" } instead initializes an existing named type. Both remain shallow with respect to referenced values.

</details>

In the agents: Agent request/result records give data a clear shape. Readonly properties and with copies do not freeze nested lists.

## 6. Compose objects through small contracts

- An interface is a contract. IText inner accepts an existing implementing object; that parameter does not declare inheritance.
- class PlainText : IText implements an interface. class Child : Parent inherits a base class: Child is a Parent.
- A constructor dependency expresses has-a: Brackets has an IText. Passing it manually is dependency injection; a container is optional.
- A decorator keeps the same contract, adds behavior, and forwards calls. virtual enables override; base accesses inherited behavior.
- sealed blocks subclasses; sealed override blocks further overriding of that member. Keep simple transformations as functions when state is unnecessary.

```csharp
IText source = new PlainText();
IText decorated = new Brackets(source);
Console.WriteLine(decorated.Read());
interface IText { string Read(); }
sealed class PlainText : IText
{
    public string Read() => "hello";
}
sealed class Brackets(IText inner) : IText
{
    public string Read() => $"[{inner.Read()}]";
}
```

**Watch:** Implements, inherits and receives are three different relationships. A constructor parameter alone never means is-a.

<details><summary>Python connection</summary>

```python
class PlainText:
    def read(self):
        return "hello"
class Brackets:
    def __init__(self, inner):
        self.inner = inner
    def read(self):
        return f"[{self.inner.read()}]"
source = PlainText()
print(Brackets(source).read())
```

</details>

<details><summary>More symbols, briefly</summary>

- **Dependency injection without machinery:** new Brackets(new PlainText()) supplies a dependency through a constructor. A framework container may automate construction and lifetimes, but is not the definition of dependency injection.
- **Base behavior:** class Friendly : Greeting { public override string Say() => base.Say() + "!"; } calls the base implementation, then adds behavior. The base method must permit overriding.
- **abstract:** An abstract class cannot be instantiated directly. An abstract method declares a required implementation without a body; a concrete derived class must override it.
- **Contract size:** Keep interfaces small and tied to actual substitution needs. A pure calculation usually fits a function; an object is useful for associated state or interchangeable behavior.

</details>

In the agents: Generated chat wrappers accept a client, add tracing or request behavior, and forward calls through a shared client contract.

## 7. Choose a container by its job

- List<T> grows; T is the element type. Arrays have fixed length. Dictionary<TKey,TValue> maps keys; HashSet<T> keeps unique values.
- IEnumerable<T> promises enumeration, not stored contents. IReadOnlyList<T> exposes indexing and Count without mutation methods through that interface.
- A read-only interface does not freeze the underlying object or its elements. Another reference may still change them.
- new() gets its type from context. [a, b] is a collection expression requiring a target type; new[] infers an array.
- Extension methods are static methods callable with instance syntax. In a traditional declaration, this marks the first receiver parameter.

```csharp
List<string> fruit = ["pear", "plum", "pear"];
IReadOnlyList<string> view = fruit;
HashSet<string> unique = new(fruit);
Dictionary<string, int> prices = new() { ["pear"] = 2 };
fruit.Add("apple");
Console.WriteLine(view.Count);
Console.WriteLine(unique.Count);
Console.WriteLine(prices["pear"]);
```

**Watch:** IReadOnlyList<T> means this access path cannot mutate through that interface; it is not a deeply immutable snapshot.

<details><summary>Python connection</summary>

```python
fruit = ["pear", "plum", "pear"]
view = fruit
unique = set(fruit)
prices = {"pear": 2}
fruit.append("apple")
print(len(view))
print(len(unique))
print(prices["pear"])
```

</details>

<details><summary>More symbols, briefly</summary>

- **Generic notation:** List<string> chooses string for the type parameter T. Dictionary<string, int> has two parameters: key type and value type. The angle brackets are type arguments, not comparisons.
- **Lookup with TryGetValue:** if (prices.TryGetValue("pear", out var price)) uses a value only when its key exists. Direct prices["missing"] throws when the key is absent.
- **Comparer choices:** new HashSet<string>(StringComparer.OrdinalIgnoreCase) treats "Red" and "red" as the same key. Choose this deliberately; default string comparison distinguishes them.
- **Three bracket uses:** items[0] indexes; int[] items = [1, 2] creates a target-typed collection; items is [1, ..] tests a list pattern. Context changes the meaning of the same brackets.
- **Inference shapes:** List<int> values = new(); gets the construction type from the declaration. var values = new List<int>(); gets the variable type from the construction. var values = []; lacks the required target type.

</details>

In the agents: Stores expose collections of records, use dictionaries for lookup and sets for deduplication, and pass enumeration contracts to helpers.

## 8. A query is a recipe, not its results

- Where filters; Any answers existence; Distinct removes duplicates; OrderByDescending sorts largest first. Comparers control equality or ordering where supplied.
- FirstOrDefault returns the first item, or default(T): often null for references, zero for int. Default does not prove absence.
- Many IEnumerable LINQ operations are deferred. foreach or a terminal operation executes the query; storing its variable does not cache results.
- ToList and ToArray enumerate now and store a shallow snapshot. Re-enumerating a deferred query can repeat work and observe source changes.
- Python generators also defer work, but a generator object is usually one-shot. A typical LINQ-to-Objects query can be enumerated again.

```csharp
var sizes = new List<int> { 1, 3, 3 };
var query = sizes.Where(size => size > 1).Distinct();
var snapshot = query.ToList();
sizes.Add(4);
Console.WriteLine(string.Join(",", query.OrderByDescending(x => x)));
Console.WriteLine(string.Join(",", snapshot));
Console.WriteLine(query.Any());
Console.WriteLine(Array.Empty<int>().FirstOrDefault());
```

**Watch:** Deferred does not mean async or background execution. Sorting still needs to examine its input when the query is enumerated.

<details><summary>Python connection</summary>

```python
sizes = [1, 3, 3]
def query():
    return dict.fromkeys(size for size in sizes if size > 1)
snapshot = list(query())
sizes.append(4)
print(",".join(map(str, sorted(query(), reverse=True))))
print(",".join(map(str, snapshot)))
print(bool(query()))
print(next(iter([]), 0))
```

</details>

<details><summary>More symbols, briefly</summary>

- **Select projects:** values.Select(x => x * 2) transforms each element when enumerated. Where selects which elements remain; Select chooses each result’s shape.
- **Any versus a count:** Any(predicate) stops after finding a match. Count(predicate) counts every match. Use Any when the question is only whether a match exists.
- **Distinct with a comparer:** names.Distinct(StringComparer.OrdinalIgnoreCase) treats differently cased versions as duplicates. The default string comparer treats them as different.
- **Source mutation and snapshots:** Changing a List before a later enumeration can change query results. Changing it while an active enumerator is traversing it may throw; a list is not a concurrent snapshot.
- **Default is type-dependent:** FirstOrDefault() returns null for an empty string sequence but 0 for an empty int sequence. First() throws instead; modern overloads can also accept an explicit fallback.

</details>

In the agents: Generated stores filter and rank local data with LINQ; materialization determines whether work repeats and which data version is observed.

## 9. Text, numbers and small checks

- `int` and `long` are whole numbers; `double` uses binary floating point; `decimal` suits decimal arithmetic.
- Integer division drops the fraction. Convert before dividing; combine totals before calculating a combined ratio.
- `$"Hi {name}"` interpolates; `@"a\b"` keeps backslashes; raw strings use three or more quotation marks.
- `is` and `switch` patterns inspect values or shapes. Regular expressions inspect patterns inside text.
- `values[^1]` means last. An array range copies elements; slicing a `Span<T>` creates a view.

```csharp
int doneA = 1, totalA = 2;
int doneB = 9, totalB = 10;
decimal combined = (decimal)(doneA + doneB)
    / (totalA + totalB);
Console.WriteLine(combined > 0.8m);
```

**Watch:** Python integer / produces a fraction; C# int / int produces an integer. Casting the already-truncated answer is too late.

<details><summary>Python connection</summary>

```python
done_a, total_a = 1, 2
done_b, total_b = 9, 10
combined = (done_a + done_b) / (total_a + total_b)
print(combined > 0.8)
# Python / keeps a fraction; use Decimal for decimal arithmetic.
```

</details>

<details><summary>More symbols, briefly</summary>

- **Numbers:** `int`: 32-bit signed; `long`: 64-bit signed, e.g. `9L`. `0.5` is double; `0.5m` is decimal. Neither makes every fraction exact.
- **DateOnly:** `new DateOnly(2026, 9, 8)` is a calendar date, with no time or timezone. Comparable to Python datetime.date.
- **StringBuilder:** `new StringBuilder().Append("a").Append("b").ToString()` builds text through a mutable buffer; useful for repeated additions.
- **Raw strings:** `"""{"name":"Ava"}"""` holds JSON text without escaping its internal quotes. Multiline raw strings use delimiter placement and indentation rules.
- **Property and list patterns:** `name is { Length: > 0 }` checks a non-null value and its property. `items is [1, ..]` checks a list starting with 1.
- **switch expression:** `count switch { 0 => "none", 1 => "one", _ => "many" }` selects a result; `_` is the fallback.
- **Regex:** `Regex.IsMatch(text, @"\A\d+\z")` checks that the entire text contains one or more digits. The regex library interprets this string.
- **Ranges and spans:** `values[1..3]` includes indexes 1 and 2; the end is excluded. A mutable span view shares the original array storage.
- **Weighted ratios:** Averaging 1/2 and 9/10 gives 0.7. Combined progress is 10/12, about 0.8333. Check for a zero denominator.
- **enum:** `enum Size { Small, Large }` defines named constants in a distinct value type. Use `Size.Small`; an enum is not a string or a class instance.

</details>

In the agents: The generated applications format text, match input shapes, parse dates and calculate evidence from totals using these ordinary operations.

## 10. A task is a promise of completion

- `Task` tracks completion, failure or cancellation. `Task<T>` also supplies a successful result of type T.
- `async` enables `await`. Await obtains the result or propagates an exception; an incomplete await can suspend the method.
- Calling a normal C# async method starts it immediately, until an incomplete await. A Python coroutine call behaves differently.
- `Task.WhenAll` represents completion of every supplied task. It does not promise separate threads.
- An already-completed task needs no suspension. Prefer await over blocking `.Result` or `.Wait()`.

```csharp
Task<int> pending = DoubleAsync(3);
Console.WriteLine("called");
Console.WriteLine(await pending);
static async Task<int> DoubleAsync(int number)
{
    Console.WriteLine("entered");
    await Task.CompletedTask;
    return number * 2;
}
```

**Watch:** A task is neither its eventual value nor a dedicated thread. Await can resume immediately when the task is complete.

<details><summary>Python connection</summary>

```python
import asyncio

async def double(number):
    return number * 2

# Inside async Python code:
pending = double(3)  # a coroutine; body has not started
result = await pending
# asyncio.create_task(double(3)) schedules it to run.
```

</details>

<details><summary>More symbols, briefly</summary>

- **Execution order:** The example prints entered, called, 6. The call executes the method body; awaiting CompletedTask does not pause it.
- **Concurrency:** Start independent operations, then await Task.WhenAll(a, b). Their waits may overlap. CPU parallelism requires separate consideration.
- **WhenAll results:** For Task<T> inputs, the resulting array follows input task order, even if operations finish in another order.
- **Python comparison:** asyncio.gather waits for multiple awaitables. Python create_task schedules a coroutine; it does not synchronously run its body by default.
- **Not every Task is hot:** Normal async APIs return already-started operations. An explicitly constructed new Task(...) can be unstarted; that is not the pattern taught here.
- **ValueTask<T>:** A value-type awaitable used by some library APIs: `int n = await ReadAsync();`. Consume each returned ValueTask once; it may avoid a Task allocation for synchronous completion, but is not a universal optimization.

</details>

In the agents: Agent request methods await model calls and tool work; they can suspend while waiting without creating one thread per request.

## 11. Stop requests and clean up

- `CancellationTokenSource` requests cancellation; its token lets work observe that request.
- `CancelAfter` requests cancellation after a deadline. Linked sources combine multiple cancellation requests.
- Pass the token into cancellable operations or call `ThrowIfCancellationRequested`; cancellation does not forcibly kill work.
- `using var` disposes at scope exit. `await using` awaits asynchronous disposal. Garbage collection serves a different purpose.
- `catch` handles exceptions; `when` filters a catch. `finally` runs when leaving the try/catch construct, after any matching catch.

```csharp
using var stop = new CancellationTokenSource();
stop.Cancel();
try
{
    stop.Token.ThrowIfCancellationRequested();
    Console.WriteLine("work");
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.WriteLine("cancelled");
}
finally { Console.WriteLine("cleanup"); }
```

**Watch:** Calling Cancel requests a stop; disposing a CancellationTokenSource cleans it up but does not itself request cancellation.

<details><summary>Python connection</summary>

```python
try:
    raise ValueError("stop this example")
except ValueError:
    print("caught")
finally:
    print("cleanup")
# Python with / async with resemble scoped cleanup.
# C# CancellationToken is not Python task.cancel().
```

</details>

<details><summary>More symbols, briefly</summary>

- **Linked cancellation:** `using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, deadlineToken);` creates a source cancelled when either input cancels.
- **Deadline:** `stop.CancelAfter(TimeSpan.FromSeconds(5))` schedules a request. Work that ignores its token can continue beyond that time.
- **Disposal versus memory:** Dispose releases resources or completes a scope promptly. The garbage collector later reclaims managed memory; using does not force collection.
- **Async disposal:** `await using var resource = ...;` awaits DisposeAsync at scope exit; the resource must support asynchronous disposal.
- **Exceptions across await:** A failing async method normally faults its task. Await rethrows the failure at the await expression; surrounding catch can handle it.
- **Exception filters:** `catch (OperationCanceledException) when (token.IsCancellationRequested)` handles that type only if the condition is true. Otherwise the search continues.
- **Rethrowing:** Inside catch, `throw;` rethrows while preserving the original exception stack. Do not catch errors merely to hide them.

</details>

In the agents: Requests use deadlines and browser-disconnect tokens, while using/finally scopes close resources and tracing spans on success or failure.

## 12. One value at a time

- An `IEnumerable<T>` iterator uses `yield return`; its body runs during enumeration, not when the iterator is obtained.
- `IAsyncEnumerable<T>` allows waiting between elements. Consume it with `await foreach`.
- `WithCancellation(token)` supplies an enumeration token; `[EnumeratorCancellation]` connects it to an async iterator parameter.
- `ConfigureAwait(false)` avoids requesting the captured context. It does not choose or create a background thread.
- `foreach` and `await foreach` dispose supported enumerators when their loops exit, including early break.

```csharp
await foreach (int number in CountAsync())
    Console.WriteLine(number);
static async IAsyncEnumerable<int> CountAsync()
{
    yield return 1;
    await Task.CompletedTask;
    yield return 2;
}
```

**Watch:** An async iterator does not make the HTTP response streaming by itself. The server and browser must also exchange and consume chunks.

<details><summary>Python connection</summary>

```python
import asyncio

async def count():
    yield 1
    await asyncio.sleep(0)
    yield 2

# Inside async Python code:
# async for number in count():
#     print(number)
```

</details>

<details><summary>More symbols, briefly</summary>

- **Async iterator timing:** Calling an async iterator obtains a sequence without executing its body. Enumeration drives that body, unlike a normal async Task method.
- **Lazy is not cached:** An iterator may rerun its body on each enumeration. Storing IEnumerable<T> does not materialize its results.
- **Incremental versus complete:** A stream can deliver values as they become available. A Task<List<T>> produces a complete list before the caller receives its result.
- **Cooperative async iterator:** Use `async IAsyncEnumerable<int> Read([EnumeratorCancellation] CancellationToken token = default)`. Check token or pass it to awaited work.
- **Consumer token:** `await foreach (var x in Read().WithCancellation(token))` passes the token to enumeration. The producer must consume it.
- **ConfigureAwait:** `await operation.ConfigureAwait(false)` controls continuation-context capture for that await. ASP.NET Core normally has no custom synchronization context.
- **Breaking an async loop:** The loop awaits asynchronous enumerator disposal. A producer finally block can run then. This does not finish producing remaining values.

</details>

In the agents: Model updates and browser NDJSON can be consumed incrementally; other routes collect the stream before returning one complete response.

## 13. Project files, paths and settings

- Namespaces group type names. `using System.Text;` imports names for lookup; it does not create objects or dispose resources.
- `Path.Combine` joins path parts. `AppContext.BaseDirectory` locates the application base, which can differ from the working directory.
- The `.csproj` declares target framework, packages and copied content. NuGet supplies packages; using directives do not install them.
- `IConfiguration` reads settings through providers. For the same key, the provider added later takes precedence.
- Inspect the actual provider order. Keep credentials separate from ordinary settings, committed sample files and printed output.

```csharp
using Microsoft.Extensions.Configuration;
var first = new Dictionary<string, string?> { ["Color"] = "blue" };
var later = new Dictionary<string, string?> { ["Color"] = "green" };
IConfiguration config = new ConfigurationBuilder()
    .AddInMemoryCollection(first)
    .AddInMemoryCollection(later)
    .Build();
Console.WriteLine(config["Color"]);
```

**Watch:** Changing your current working directory can break relative file paths. Merely putting a data file beside source does not guarantee deployment.

<details><summary>Python connection</summary>

```python
first = {"Color": "blue"}
later = {"Color": "green"}
config = {**first, **later}
print(config["Color"])
# Analogy: later values replace matching earlier keys.
# .NET providers are richer than this dictionary merge.
```

</details>

<details><summary>More symbols, briefly</summary>

- **SDK → build → run:** The SDK includes compiler and build tools. `dotnet build` compiles the project; `dotnet run` builds as needed and starts it.
- **Runtime and ASP.NET Core:** .NET runs compiled C# code. ASP.NET Core adds web hosting, HTTP routes and middleware; they are framework capabilities.
- **PackageReference:** A `.csproj` PackageReference names a NuGet dependency and version. Restore resolves it; a namespace import only shortens source names.
- **Content copying:** Inside ItemGroup, `<None Update="Data/*.json" CopyToOutputDirectory="PreserveNewest" />` copies matching existing project items into build output.
- **Publish separately:** Use CopyToPublishDirectory when content must be included in published output. Verify deployed paths; build output and publishing are different stages.
- **Runtime paths:** `Path.Combine(AppContext.BaseDirectory, "Data", "colors.json")` locates deployed content without assuming the shell started in the project folder.
- **Settings sources:** Providers may read JSON, environment variables, command-line arguments or other sources. Framework defaults and explicit Add... calls determine order.
- **Namespaces versus folders:** `namespace Learning;` assigns type names to a namespace. Folder names are a convention and do not automatically define namespaces.
- **Example context:** The example uses Microsoft.Extensions.Configuration from the ASP.NET Core shared framework; it reads only in-memory dictionaries.

</details>

In the agents: Generated apps copy their local data into output, resolve it at startup and load model/observability configuration through explicit provider calls.

## 14. Objects cross the JSON boundary

- `Serialize` turns an object into JSON text; `Deserialize<T>` attempts to build a T from JSON.
- A DTO is a data-transfer shape, often a record. It is not automatically a complete validation policy.
- Nullable annotations guide compiler analysis. Runtime input still needs explicit checks and appropriate serializer options.
- Attributes attach metadata. Reflection lets code inspect types, methods and metadata; libraries decide how to use it.
- JSON source generation creates serializer support during compilation. `partial` lets generated and handwritten declarations form one type.

```csharp
using System.Text.Json;
string json = """{"Name":"Mira","Age":7}""";
Person? person = JsonSerializer.Deserialize<Person>(json);
if (person is null || string.IsNullOrWhiteSpace(person.Name)
    || person.Age < 0)
    throw new ArgumentException("Invalid person");
Console.WriteLine(person.Name);
record Person(string Name, int Age);
```

**Watch:** A property declared string can still receive invalid external data. Types, serializer settings and application validation each do different jobs.

<details><summary>Python connection</summary>

```python
import json

person = json.loads('{"Name":"Mira","Age":7}')
if not person.get("Name") or person["Age"] < 0:
    raise ValueError("Invalid person")
print(person["Name"])
# Python returns a dictionary here, not a typed C# record.
```

</details>

<details><summary>More symbols, briefly</summary>

- **Serialization:** `JsonSerializer.Serialize(new Person("Mira", 7))` creates JSON text. Property naming and enum handling depend on configured options.
- **Nullable enforcement:** System.Text.Json can opt into limited RespectNullableAnnotations checks. That still does not validate every missing value or business rule.
- **Attributes:** `[Description("Counts beads")]` describes a method. It cannot execute the method, validate its arguments or expose it as a tool by itself.
- **Reflection:** `typeof(Counter).GetMethods()` inspects method metadata at runtime. A library may use it to build tool descriptions or invoke registered methods.
- **Source-generated JSON:** `[JsonSerializable(typeof(Person))] partial class PeopleJson : JsonSerializerContext { }` requests generated JSON metadata/support.
- **Actually using the context:** Pass `PeopleJson.Default.Person` to the appropriate JsonSerializer overload. Declaring a context alone does not redirect every serializer call.
- **Two meanings of generated:** The C# compiler can generate serializer source. A packaging script can copy skill files. These are separate operations with different owners.

</details>

In the agents: Browser DTOs and model tool arguments cross JSON boundaries; attributes describe tools and generated serializer contexts support selected data types.

## 15. The browser talks HTTP

- ASP.NET Core maps HTTP routes to handlers. Binding turns request values into handler arguments.
- Validate input before acting. `Results.Ok` creates a 200 response; `Results.BadRequest` creates a 400 response.
- Middleware surrounds request handling and can continue to the next step or return a response early.
- Browser JavaScript uses fetch to send HTTP requests. The browser does not directly call an ordinary C# method.
- A process can serve many requests. Session state spans requests only when the application deliberately stores and identifies it.

```csharp
// ASP.NET Core: register a route; this excerpt starts no server.
var builder = WebApplication.CreateBuilder();
var app = builder.Build();
app.MapGet("/square/{value:int}", (int value) =>
{
    if (value < 0 || value > 100)
        return Results.BadRequest(new { error = "Use 0 to 100" });
    return Results.Ok(new { answer = value * value });
});
```

**Watch:** Streaming inside the server does not guarantee streaming to the browser. The HTTP route and JavaScript consumer decide that boundary.

<details><summary>Python connection</summary>

```python
# Analogy inside a Python web framework:
# @app.get("/square/{value}")
# def square(value: int):
#     ...validate value...
#     return {"answer": value * value}
# Routing and HTTP responses come from the framework,
# not Python or C# syntax alone.
```

</details>

<details><summary>More symbols, briefly</summary>

- **Binding:** A route placeholder can bind an int; a POST body can bind a DTO; registered services can be injected as parameters.
- **Route constraints:** In /square/{value:int}, the int route constraint helps choose a matching route. It does not enforce the allowed 0–100 range.
- **Results:** Returning Results.Ok(new { answer = 9 }) lets the framework execute an HTTP result and serialize its body. A return value is not automatically a network write at that exact line.
- **Middleware:** `app.Use(async (context, next) => { /* before */ await next(context); /* after */ });` illustrates code around the next request stage.
- **Browser JSON:** `const response = await fetch(url); const data = await response.json();` waits for the response body and parses one JSON document. Check response.ok explicitly.
- **NDJSON:** Newline-delimited JSON sends one JSON value per line. Browser code must buffer chunks until complete lines arrive; chunk boundaries need not match lines.
- **Provider SSE:** Server-sent events have their own text framing, such as data: lines and blank-line separators. A provider SSE stream and a browser NDJSON stream are separate protocols.
- **Three lifetimes:** Startup-created objects may live for the process. Local handler variables live for that request. Conversation/session data needs an explicit storage policy.
- **Example context:** This ASP.NET Core excerpt only registers a handler. A complete web app calls app.Run() to listen; the learning game starts no server.

</details>

In the agents: The agent UIs use fetch; server endpoints validate DTOs, execute request-specific work and return JSON or explicitly framed incremental results.

## 16. Small functions, clear dependencies

- A model tool is an ordinary function exposed through library registration and metadata. Tool execution is application code.
- Passing a dependency into a constructor or method is dependency injection; a container can automate constructing and supplying it.
- Container lifetimes choose reuse: singleton per container, scoped per scope, transient per resolution.
- `ActivitySource.StartActivity` may return null. Use scoped disposal and record useful metadata without secrets or sensitive content.
- Offline tests prove local behavior with controlled dependencies. Live smokes check real integration; neither proves every possible outcome.

```csharp
ICounter counter = new FixedCounter();
Console.WriteLine(Describe(counter));
static string Describe(ICounter counter) => $"{counter.Count()} beads";
interface ICounter { int Count(); }
sealed class FixedCounter : ICounter
{
    public int Count() => 3;
}
```

**Watch:** An interface or model-tool attribute does not add intelligence or reliability by itself. The implementation, validation and tests still determine behavior.

<details><summary>Python connection</summary>

```python
def describe(count):
    return f"{count()} beads"

print(describe(lambda: 3))
# Python can inject a function directly.
# C# also supports delegates; ICounter demonstrates
# an explicit contract with a replaceable object.
```

</details>

<details><summary>More symbols, briefly</summary>

- **Deterministic core:** `static int Add(int a, int b) => a + b;` is ordinary calculation. A tool wrapper adds a callable schema, arguments and dispatch around it.
- **IChatClient analogy:** The real agents depend on a chat-client interface. ICounter is a tiny stand-in for the same principle: supply an implementation of a contract.
- **Manual versus container:** `new Greeter(counter)` is manual injection. Registering ICounter and resolving Greeter lets a DI container supply that constructor argument.
- **Singleton:** One instance is reused per container. In a web app it can be used by simultaneous requests; the lifetime does not make mutable code thread-safe.
- **Scoped and transient:** Scoped means one instance per explicit scope, commonly one HTTP request. Transient means a fresh instance each time the container resolves it.
- **Telemetry scope:** `using var activity = source.StartActivity("count"); activity?.SetTag("item.count", 3);` marks a bounded operation when tracing is enabled.
- **Privacy and evidence:** Counts, durations and status can explain work without logging raw prompts, private text or credentials. A created Activity does not prove remote export.
- **Test seam:** The example supplies FixedCounter. An offline test can assert Describe returns 3 beads without calling a service; a live service check is separate.
- **lock:** `lock (gate) { count++; }` protects a critical section from other threads using the same gate. Keep it short; C# forbids await inside lock.
- **Interlocked:** `Interlocked.Increment(ref count)` makes that single increment atomic. Ordinary count++ is not atomic; an atomic increment does not protect a larger sequence of operations.

</details>

In the agents: The generated agents combine deterministic tools with replaceable clients and metadata tracing. Their offline tests and live model checks cover different boundaries.

## Keep the design small

Use functions for transformations; records for data; classes for state plus operations. Pass dependencies explicitly. Introduce interfaces or delegates when replacing a dependency helps. Inheritance is one option, not a prerequisite.

## Official references

- [Object-oriented building blocks](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/object-oriented/)
- [Primary constructors](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors)
- [Records and equality](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records)
- [LINQ queries and execution](https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/introduction-to-linq-queries)
- [Async and await](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/)
- [Async streams](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream)
- [Resource disposal with using](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using)
- [Ranges and indexes](https://learn.microsoft.com/en-us/dotnet/csharp/tutorials/ranges-indexes)
- [JSON nullable annotations](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations)
- [JSON source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [Configuration providers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)
- [Minimal API binding](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding?view=aspnetcore-10.0)
- [Dependency lifetimes](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes)

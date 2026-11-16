using ViShap.Viper.Engine;

namespace ViShap.Viper.Serialization.Tests.Fixtures;

/// <summary>
/// A trace that records nothing. Setting it on an operation makes the engine read the members of a type
/// through its contract's methods, as it does for the dumper, rather than through an accessor table.
/// </summary>
internal sealed class SilentTrace : IWireTrace
{
    public void Label(string name) { }

    public void LabelIndex(int index) { }

    public void LabelMapKey(int index) { }

    public void LabelItem(int item) { }

    public void LabelMapValue(int index) { }

    public void Begin(Type declaredType, long offset) { }

    public void Continue() { }

    public void Null() { }

    public void Reference(int id, bool back) { }

    public void Shape(TraceShape kind, int count) { }

    public void Union(byte tag, Type runtimeType) { }

    public void Value<T>(T value) { }

    public void Field(int key, int length, long offset) { }

    public void EndField(bool known, long offset) { }

    public void End(long offset) { }
}

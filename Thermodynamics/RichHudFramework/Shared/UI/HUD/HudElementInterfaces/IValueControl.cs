namespace RichHudFramework.UI
{
    public interface IValueControl
    {
        event EventHandler ValueChanged;

        EventHandler UpdateValueCallback { set; }
    }

    public interface IValueControl<T> : IValueControl
    {
        T Value { get; }
    }
}
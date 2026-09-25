using System;
using System.Collections.Generic;
using VRage;
using VRageMath;

namespace RichHudFramework
{
    using EventData = MyTuple<bool, Action>;

    namespace UI.Client
    {
        public sealed partial class BindManager
        {
            private partial class BindGroup
            {
                public class Bind : IBind
                {
                    public string Name => _instance.GetOrSetBindMemberFunc(index, null, (int)BindAccesssors.Name) as string;

                    public int Index => index.Y;

                    public int AliasCount => (int)_instance.GetOrSetBindMemberFunc(index, null, (int)BindAccesssors.AliasCount);

                    public bool Analog => (bool)_instance.GetOrSetBindMemberFunc(index, null, (int)BindAccesssors.Analog);

                    public float AnalogValue => (float)_instance.GetOrSetBindMemberFunc(index, null, (int)BindAccesssors.AnalogValue);

                    public bool IsPressed => _instance.IsBindPressedFunc(index, (int)BindAccesssors.IsPressed);

                    public bool IsPressedAndHeld => _instance.IsBindPressedFunc(index, (int)BindAccesssors.IsPressedAndHeld);

                    public bool IsNewPressed => _instance.IsBindPressedFunc(index, (int)BindAccesssors.IsNewPressed);

                    public bool IsReleased => _instance.IsBindPressedFunc(index, (int)BindAccesssors.IsReleased);

                    public event EventHandler NewPressed;

                    public event EventHandler PressedAndHeld;

                    public event EventHandler Released;

                    private readonly Vector2I index;


                    public Bind(Vector2I index)
                    {
                        this.index = index;
                        _instance.GetOrSetBindMemberFunc(index, new EventData(true, OnNewPressed), (int)BindAccesssors.OnNewPress);
                        _instance.GetOrSetBindMemberFunc(index, new EventData(true, OnPressedAndHeld), (int)BindAccesssors.OnPressAndHold);
                        _instance.GetOrSetBindMemberFunc(index, new EventData(true, OnReleased), (int)BindAccesssors.OnRelease);
                    }


                    private void OnNewPressed()
                    {
                        NewPressed?.Invoke(this, EventArgs.Empty);
                    }


                    private void OnPressedAndHeld()
                    {
                        PressedAndHeld?.Invoke(this, EventArgs.Empty);
                    }


                    private void OnReleased()
                    {
                        Released?.Invoke(this, EventArgs.Empty);
                    }


                    public List<ControlHandle> GetCombo(int alias = 0)
                    {
                        var indices = _instance.GetOrSetBindMemberFunc(index, alias, (int)BindAccesssors.GetCombo) as List<int>;

                        var combo = new List<ControlHandle>(indices.Count);

                        for (int n = 0; n < indices.Count; n++)
                            combo.Add((ControlHandle)indices[n]);

                        return combo;
                    }


                    public List<int> GetConIDs(int alias = 0) =>
                        _instance.GetOrSetBindMemberFunc(index, alias, (int)BindAccesssors.GetCombo) as List<int>;


                    public bool TrySetCombo(IReadOnlyList<ControlHandle> combo, int alias = 0, bool isStrict = true, bool isSilent = true)
                    {
                        var comboData = new MyTuple<IReadOnlyList<int>, int, bool, bool>(GetComboIndicesTemp(combo), alias, isStrict, isSilent);
                        return (bool)_instance.GetOrSetBindMemberFunc(index, comboData, (int)BindAccesssors.TrySetComboWithIndices);
                    }


                    public bool TrySetCombo(IReadOnlyList<int> combo, int alias = 0, bool isStrict = true, bool isSilent = true)
                    {
                        var comboData = new MyTuple<IReadOnlyList<int>, int, bool, bool>(combo, alias, isStrict, isSilent);
                        return (bool)_instance.GetOrSetBindMemberFunc(index, comboData, (int)BindAccesssors.TrySetComboWithIndices);
                    }


                    public bool TrySetCombo(IReadOnlyList<string> combo, int alias = 0, bool isStrict = true, bool isSilent = true)
                    {
                        var comboData = new MyTuple<IReadOnlyList<string>, int, bool, bool>(combo, alias, isStrict, isSilent);
                        return (bool)_instance.GetOrSetBindMemberFunc(index, comboData, (int)BindAccesssors.TrySetComboWithNames);
                    }


                    public void ClearCombo(int alias = 0) =>
                        _instance.GetOrSetBindMemberFunc(index, alias, (int)BindAccesssors.ClearCombo);


                    public void ClearSubscribers() =>
                        _instance.GetOrSetBindMemberFunc(index, null, (int)BindAccesssors.ClearSubscribers);


                    public override bool Equals(object obj)
                    {
                        return ((Bind)obj).index == index;
                    }


                    public override int GetHashCode()
                    {
                        return index.GetHashCode();
                    }
                }
            }
        }
    }
}
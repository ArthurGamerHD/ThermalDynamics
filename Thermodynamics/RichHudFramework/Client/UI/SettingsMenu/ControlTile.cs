using System;
using System.Collections;
using System.Collections.Generic;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework.UI.Client
{
    using ControlContainerMembers = MyTuple<
        ApiMemberAccessor, // GetOrSetMember,
        MyTuple<object, Func<int>>, // Member List
        object // ID
    >;
    using ControlMembers = MyTuple<
        ApiMemberAccessor, // GetOrSetMember
        object // ID
    >;

    public class ControlTile : IControlTile
    {
        public IReadOnlyList<TerminalControlBase> Controls { get; }

        public IControlTile ControlContainer => this;

        public bool Enabled
        {
/// <summary>return operation.</summary>
            get { return (bool)GetOrSetMemberFunc(null, (int)ControlTileAccessors.Enabled); }
/// <summary>Returns the orsetmemberfunc.</summary>
            set { GetOrSetMemberFunc(value, (int)ControlTileAccessors.Enabled); }
        }

        public object ID => tileMembers.Item3;

        private ApiMemberAccessor GetOrSetMemberFunc => tileMembers.Item1;
        private readonly ControlContainerMembers tileMembers;

/// <summary>ControlTile operation.</summary>
        public ControlTile() : this(RichHudTerminal.Instance.GetNewMenuTile())
        { }

/// <summary>ControlTile operation.</summary>
        public ControlTile(ControlContainerMembers data)
        {
            tileMembers = data;

            var GetControlDataFunc = data.Item2.Item1 as Func<int, ControlMembers>;
            Func<int, TerminalControlBase> GetControlFunc = (x => new TerminalControl(GetControlDataFunc(x)));

/// <summary>ReadOnlyApiCollection operation.</summary>
            Controls = new ReadOnlyApiCollection<TerminalControlBase>(GetControlFunc, data.Item2.Item2);
        }

        IEnumerator<ITerminalControl> IEnumerable<ITerminalControl>.GetEnumerator() =>
            Controls.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            Controls.GetEnumerator();

/// <summary>Adds a .</summary>
        public void Add(TerminalControlBase control) =>
            GetOrSetMemberFunc(control.ID, (int)ControlTileAccessors.AddControl);

/// <summary>Returns the apidata.</summary>
        public ControlContainerMembers GetApiData() =>
            tileMembers;

        private class TerminalControl : TerminalControlBase
        {
/// <summary>TerminalControl operation.</summary>
            public TerminalControl(ControlMembers data) : base(data)
            { }
        }
    }
}
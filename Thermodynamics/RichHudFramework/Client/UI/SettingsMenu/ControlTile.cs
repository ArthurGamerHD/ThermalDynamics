using System;
using System.Collections;
using System.Collections.Generic;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework.UI.Client
{
    using ControlContainerMembers = MyTuple<
        ApiMemberAccessor,
        MyTuple<object, Func<int>>,
        object
    >;
    using ControlMembers = MyTuple<
        ApiMemberAccessor,
        object
    >;

    public class ControlTile : IControlTile
    {
        public IReadOnlyList<TerminalControlBase> Controls { get; }

        public IControlTile ControlContainer => this;

        public bool Enabled
        {

            get { return (bool)GetOrSetMemberFunc(null, (int)ControlTileAccessors.Enabled); }

            set { GetOrSetMemberFunc(value, (int)ControlTileAccessors.Enabled); }
        }

        public object ID => tileMembers.Item3;

        private ApiMemberAccessor GetOrSetMemberFunc => tileMembers.Item1;
        private readonly ControlContainerMembers tileMembers;


        public ControlTile() : this(RichHudTerminal.Instance.GetNewMenuTile())
        { }


        public ControlTile(ControlContainerMembers data)
        {
            tileMembers = data;

            var GetControlDataFunc = data.Item2.Item1 as Func<int, ControlMembers>;
            Func<int, TerminalControlBase> GetControlFunc = (x => new TerminalControl(GetControlDataFunc(x)));


            Controls = new ReadOnlyApiCollection<TerminalControlBase>(GetControlFunc, data.Item2.Item2);
        }

        IEnumerator<ITerminalControl> IEnumerable<ITerminalControl>.GetEnumerator() =>
            Controls.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            Controls.GetEnumerator();


        public void Add(TerminalControlBase control) =>
            GetOrSetMemberFunc(control.ID, (int)ControlTileAccessors.AddControl);


        public ControlContainerMembers GetApiData() =>
            tileMembers;

        private class TerminalControl : TerminalControlBase
        {

            public TerminalControl(ControlMembers data) : base(data)
            { }
        }
    }
}
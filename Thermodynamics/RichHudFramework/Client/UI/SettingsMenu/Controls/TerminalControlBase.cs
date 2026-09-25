using System;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework.UI.Client
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor,
		object
	>;

	public abstract class TerminalControlBase : ITerminalControl
	{
		public event EventHandler ControlChanged;

		public EventHandler ControlChangedHandler { set { ControlChanged += value; } }

		public string Name
		{

			get { return GetOrSetMember(null, (int)TerminalControlAccessors.Name) as string; }

			set { GetOrSetMember(value, (int)TerminalControlAccessors.Name); }
		}

		public bool Enabled
		{

			get { return (bool)GetOrSetMember(null, (int)TerminalControlAccessors.Enabled); }

			set { GetOrSetMember(value, (int)TerminalControlAccessors.Enabled); }
		}

		public ToolTip ToolTip
		{
			get { return _toolTip; }

			set { _toolTip = value; GetOrSetMember(value.GetToolTipFunc, (int)TerminalControlAccessors.ToolTip); }
		}

		public object ID { get; }

		protected readonly ApiMemberAccessor GetOrSetMember;

		protected ToolTip _toolTip;


		public TerminalControlBase(MenuControls controlEnum) : this(RichHudTerminal.Instance.GetNewMenuControl(controlEnum))
		{
			GetOrSetMember(new Action(ControlChangedCallback), (int)TerminalControlAccessors.GetOrSetControlCallback);
		}


		protected virtual void ControlChangedCallback()
		{
			if (ControlChanged == null)
				return;

			Internal.ExceptionHandler.Run(() =>
			{
				ControlChanged.Invoke(this, EventArgs.Empty);
			});
		}


		public TerminalControlBase(ControlMembers data)
		{
			GetOrSetMember = data.Item1;
			ID = data.Item2;
		}


		public ControlMembers GetApiData()
		{
			return new ControlMembers()
			{
				Item1 = GetOrSetMember,
				Item2 = ID
			};
		}
	}

	public abstract class TerminalValue<TValue> : TerminalControlBase, ITerminalValue<TValue>
	{
		public virtual TValue Value
		{

			get { return (TValue)GetOrSetMember(null, (int)TerminalControlAccessors.Value); }

			set { GetOrSetMember(value, (int)TerminalControlAccessors.Value); }
		}

		public Func<TValue> CustomValueGetter
		{

			get { return GetOrSetMember(null, (int)TerminalControlAccessors.ValueGetter) as Func<TValue>; }

			set { GetOrSetMember(value, (int)TerminalControlAccessors.ValueGetter); }
		}


		public TerminalValue(MenuControls controlEnum) : base(controlEnum)
		{ }
	}
}
using System;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework.UI.Client
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember
		object // ID
	>;

	public abstract class TerminalControlBase : ITerminalControl
	{
		public event EventHandler ControlChanged;

		public EventHandler ControlChangedHandler { set { ControlChanged += value; } }

		public string Name
		{
/// <summary>Returns the orsetmember.</summary>
			get { return GetOrSetMember(null, (int)TerminalControlAccessors.Name) as string; }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)TerminalControlAccessors.Name); }
		}

		public bool Enabled
		{
/// <summary>return operation.</summary>
			get { return (bool)GetOrSetMember(null, (int)TerminalControlAccessors.Enabled); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)TerminalControlAccessors.Enabled); }
		}

		public ToolTip ToolTip
		{
			get { return _toolTip; }
/// <summary>Returns the orsetmember.</summary>
			set { _toolTip = value; GetOrSetMember(value.GetToolTipFunc, (int)TerminalControlAccessors.ToolTip); }
		}

		public object ID { get; }

		protected readonly ApiMemberAccessor GetOrSetMember;

		protected ToolTip _toolTip;

/// <summary>TerminalControlBase operation.</summary>
		public TerminalControlBase(MenuControls controlEnum) : this(RichHudTerminal.Instance.GetNewMenuControl(controlEnum))
		{
			GetOrSetMember(new Action(ControlChangedCallback), (int)TerminalControlAccessors.GetOrSetControlCallback);
		}

/// <summary>ControlChangedCallback operation.</summary>
		protected virtual void ControlChangedCallback()
		{
			if (ControlChanged == null)
				return;

			Internal.ExceptionHandler.Run(() =>
			{
				ControlChanged.Invoke(this, EventArgs.Empty);
			});
		}

/// <summary>TerminalControlBase operation.</summary>
		public TerminalControlBase(ControlMembers data)
		{
			GetOrSetMember = data.Item1;
			ID = data.Item2;
		}

/// <summary>Returns the apidata.</summary>
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
/// <summary>return operation.</summary>
			get { return (TValue)GetOrSetMember(null, (int)TerminalControlAccessors.Value); }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)TerminalControlAccessors.Value); }
		}

		public Func<TValue> CustomValueGetter
		{
/// <summary>Returns the orsetmember.</summary>
			get { return GetOrSetMember(null, (int)TerminalControlAccessors.ValueGetter) as Func<TValue>; }
/// <summary>Returns the orsetmember.</summary>
			set { GetOrSetMember(value, (int)TerminalControlAccessors.ValueGetter); }
		}

/// <summary>TerminalValue operation.</summary>
		public TerminalValue(MenuControls controlEnum) : base(controlEnum)
		{ }
	}
}
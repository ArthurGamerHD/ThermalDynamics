using System;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor,
		object
	>;

	namespace UI
	{
		public enum TerminalControlAccessors : int
		{
			GetOrSetControlCallback = 1,

			Name = 2,

			Enabled = 3,

			ToolTip = 4,

			Value = 8,

			ValueGetter = 9,
		}

		public interface ITerminalControl
		{
			event EventHandler ControlChanged;

			EventHandler ControlChangedHandler { set; }

			string Name { get; set; }

			bool Enabled { get; set; }

			ToolTip ToolTip { get; set; }

			object ID { get; }


			ControlMembers GetApiData();
		}

		public interface ITerminalValue<TValue> : ITerminalControl
		{
			TValue Value { get; set; }

			Func<TValue> CustomValueGetter { get; set; }
		}
	}
}
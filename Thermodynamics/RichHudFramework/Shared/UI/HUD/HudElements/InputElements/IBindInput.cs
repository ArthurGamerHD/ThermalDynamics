using System;
using System.Collections.Generic;

namespace RichHudFramework.UI
{
    using Client;
    using Server;

    public interface IBindEventProxy
	{
		event EventHandler NewPressed;

		event EventHandler PressedAndHeld;

		event EventHandler Released;
	}

	public interface IBindInput : IFocusableElement, IEnumerable<IBindEventProxy>
	{
		IBindEventProxy this[IBind bind] { get; }


		void Add(IBind bind, EventHandler NewPressed = null, EventHandler PressedAndHeld = null, EventHandler Released = null);


		bool GetHasBind(IBind bind);

        bool IsFocusRequired { get; set; }

        SeBlacklistModes InputFilter { get; set; }
    }

	public interface IBindInputElement : IFocusableElement
	{
		IBindInput BindInput { get; }
	}
}
using System;
using System.Collections;
using System.Collections.Generic;
using VRageMath;

namespace RichHudFramework.UI
{
	using Client;
	using Server;

	public class BindInputElement : HudNodeBase, IBindInput
	{
		public IBindInput CollectionInitializer => this;

		public IFocusHandler FocusHandler { get; protected set; }

		public IBindEventProxy this[IBind bind] => binds[bind];

		public bool IsFocusRequired { get; set; }

        public Func<bool> InputPredicate { get; set; }

        public SeBlacklistModes InputFilter { get; set; }

		protected readonly Dictionary<IBind, BindEventProxy> binds;

/// <summary>BindInputElement operation.</summary>
		public BindInputElement(HudParentBase parent = null) : base(parent)
		{
			FocusHandler = (parent as IFocusableElement)?.FocusHandler;
			IsFocusRequired = false;
			binds = new Dictionary<IBind, BindEventProxy>();
		}

/// <summary>Adds a .</summary>
		public void Add(IBind bind, EventHandler NewPressed = null, EventHandler PressedAndHeld = null, EventHandler Released = null)
		{
			if (!binds.ContainsKey(bind))
				binds.Add(bind, new BindEventProxy());

			if (NewPressed != null || PressedAndHeld != null | Released != null)
			{
				var proxy = binds[bind];

				if (NewPressed != null)
					proxy.NewPressed += NewPressed;

				if (PressedAndHeld != null)
					proxy.PressedAndHeld += PressedAndHeld;

				if (Released != null)
					proxy.Released += Released;
			}
		}

/// <summary>Reset operation.</summary>
		public void Reset() { binds.Clear(); }

/// <summary>Returns the hasbind.</summary>
		public bool GetHasBind(IBind bind) =>
			binds.ContainsKey(bind);

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			FocusHandler = (Parent as IFocusableElement)?.FocusHandler;

			if (IsFocusRequired && !(FocusHandler?.HasFocus ?? false))
				return;

			if (!InputPredicate?.Invoke() ?? false)
				return;

			if (InputFilter != SeBlacklistModes.None)
				BindManager.RequestTempBlacklist(InputFilter);

			var owner = (object)(FocusHandler?.InputOwner) ?? Parent;

			foreach (KeyValuePair<IBind, BindEventProxy> pair in binds)
			{
				if (pair.Key.IsNewPressed)
					pair.Value.InvokeNewPressed(owner, EventArgs.Empty);

				if (pair.Key.IsPressedAndHeld)
					pair.Value.InvokePressedAndHeld(owner, EventArgs.Empty);

				if (pair.Key.IsReleased)
					pair.Value.InvokeReleased(owner, EventArgs.Empty);
			}
		}

/// <summary>Returns the enumerator.</summary>
		public IEnumerator<IBindEventProxy> GetEnumerator() =>
			binds.Values.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		protected class BindEventProxy : IBindEventProxy
		{
			public event EventHandler NewPressed;

			public event EventHandler PressedAndHeld;

			public event EventHandler Released;

/// <summary>InvokeNewPressed operation.</summary>
			public void InvokeNewPressed(object sender, EventArgs args) =>
				NewPressed?.Invoke(sender, args);

/// <summary>InvokePressedAndHeld operation.</summary>
			public void InvokePressedAndHeld(object sender, EventArgs args) =>
				PressedAndHeld?.Invoke(sender, args);

/// <summary>InvokeReleased operation.</summary>
			public void InvokeReleased(object sender, EventArgs args) =>
				Released?.Invoke(sender, args);

/// <summary>ClearSubscribers operation.</summary>
			public void ClearSubscribers()
			{
				NewPressed = null;
				PressedAndHeld = null;
				Released = null;
			}
		}
	}
}
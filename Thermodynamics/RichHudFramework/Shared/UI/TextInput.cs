using Sandbox.ModAPI;
using System;
using System.Collections.Generic;

namespace RichHudFramework.UI
{
	public class TextInput
	{
		private readonly Func<char, bool> IsCharAllowedFunc;
		private readonly Action<char> AppendAction;
		private readonly Action BackspaceAction;

/// <summary>TextInput operation.</summary>
		public TextInput(Action<char> AppendAction, Action BackspaceAction, Func<char, bool> IsCharAllowedFunc = null)
		{
			this.AppendAction = AppendAction;
			this.BackspaceAction = BackspaceAction;
			this.IsCharAllowedFunc = IsCharAllowedFunc;
		}

/// <summary>HandleInput operation.</summary>
		public void HandleInput()
		{
			IReadOnlyList<char> input = MyAPIGateway.Input.TextInput;

			if (SharedBinds.Back.IsPressedAndHeld || SharedBinds.Back.IsNewPressed)
				BackspaceAction?.Invoke();

			for (int n = 0; n < input.Count; n++)
			{
				if (input[n] != '\b' && (IsCharAllowedFunc == null || IsCharAllowedFunc(input[n])))
					AppendAction?.Invoke(input[n]);
			}
		}
	}
}
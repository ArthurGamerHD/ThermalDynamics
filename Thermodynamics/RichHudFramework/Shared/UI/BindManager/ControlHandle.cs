using System;
using VRage.Input;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Server;

namespace RichHudFramework
{
	namespace UI
	{
		public struct ControlHandle
		{
			public const int GPKeysStart = (int)RichHudControls.ReservedEnd + 1;

			public IControl Control => BindManager.GetControl(this);

			public RichHudControls ControlEnum => (RichHudControls)id;

			public readonly int id;

/// <summary>ControlHandle operation.</summary>
			public ControlHandle(string controlName)
			{
				this.id = BindManager.GetControl(controlName);
			}

/// <summary>ControlHandle operation.</summary>
			public ControlHandle(int id)
			{
				this.id = id;
			}

/// <summary>ControlHandle operation.</summary>
			public ControlHandle(MyKeys id)
			{
				this.id = (int)id;
			}

/// <summary>ControlHandle operation.</summary>
			public ControlHandle(IControl con)
			{
				this.id = con.Index;
			}

/// <summary>ControlHandle operation.</summary>
			public ControlHandle(RichHudControls id)
			{
				this.id = (int)id;
			}

/// <summary>ControlHandle operation.</summary>
			public ControlHandle(MyJoystickButtonsEnum id)
			{
				this.id = GPKeysStart + (int)id;
			}

/// <summary>ControlHandle operation.</summary>
			public static explicit operator ControlHandle(int con)
			{
				return new ControlHandle(con);
			}

/// <summary>ControlHandle operation.</summary>
			public static implicit operator ControlHandle(string controlName)
			{
				return new ControlHandle(controlName);
			}

/// <summary>ControlHandle operation.</summary>
			public static implicit operator ControlHandle(MyKeys id)
			{
				return new ControlHandle(id);
			}

/// <summary>MyKeys operation.</summary>
			public static implicit operator MyKeys(ControlHandle handle)
			{
				var id = (MyKeys)handle.id;

				if (Enum.IsDefined(typeof(MyKeys), id))
					return id;
				else
				{
					throw new Exception($"ControlHandle index {handle.id} cannot be converted to MyKeys.");
				}
			}

/// <summary>ControlHandle operation.</summary>
			public static implicit operator ControlHandle(RichHudControls id)
			{
				return new ControlHandle(id);
			}

/// <summary>RichHudControls operation.</summary>
			public static implicit operator RichHudControls(ControlHandle handle)
			{
				var id = (RichHudControls)handle.id;

				if (Enum.IsDefined(typeof(RichHudControls), id))
					return id;
				else
				{
					throw new Exception($"ControlHandle index {handle.id} cannot be converted to RichHudControls.");
				}
			}

/// <summary>ControlHandle operation.</summary>
			public static implicit operator ControlHandle(MyJoystickButtonsEnum id)
			{
				return new ControlHandle(id);
			}

/// <summary>MyJoystickButtonsEnum operation.</summary>
			public static implicit operator MyJoystickButtonsEnum(ControlHandle handle)
			{
				var id = (MyJoystickButtonsEnum)handle.id;

				if (Enum.IsDefined(typeof(MyJoystickButtonsEnum), id))
					return id;
				else
				{
					throw new Exception($"ControlHandle index {handle.id} cannot be converted to MyJoystickButtonsEnum.");
				}
			}

/// <summary>int operation.</summary>
			public static implicit operator int(ControlHandle handle)
			{
				return handle.id;
			}

/// <summary>Returns the hashcode.</summary>
			public override int GetHashCode()
			{
				return id.GetHashCode();
			}
		}
	}
}
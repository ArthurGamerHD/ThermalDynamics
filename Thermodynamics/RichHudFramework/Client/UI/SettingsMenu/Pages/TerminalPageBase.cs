using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember
		object // ID
	>;

	namespace UI.Client
	{
		public abstract class TerminalPageBase : ITerminalPage
		{
			public string Name
			{
/// <summary>Returns the orsetmemberfunc.</summary>
				get { return GetOrSetMemberFunc(null, (int)TerminalPageAccessors.Name) as string; }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)TerminalPageAccessors.Name); }
			}

			public object ID => data.Item2;

			public bool Enabled
			{
/// <summary>return operation.</summary>
				get { return (bool)GetOrSetMemberFunc(null, (int)TerminalPageAccessors.Enabled); }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value, (int)TerminalPageAccessors.Enabled); }
			}

			protected ApiMemberAccessor GetOrSetMemberFunc => data.Item1;

			protected readonly ControlMembers data;

/// <summary>TerminalPageBase operation.</summary>
			public TerminalPageBase(ModPages pageEnum)
			{
				data = RichHudTerminal.Instance.GetNewMenuPage(pageEnum);
			}

/// <summary>TerminalPageBase operation.</summary>
			public TerminalPageBase(ControlMembers data)
			{
				this.data = data;
			}

/// <summary>Returns the apidata.</summary>
			public ControlMembers GetApiData() =>
				data;
		}
	}
}
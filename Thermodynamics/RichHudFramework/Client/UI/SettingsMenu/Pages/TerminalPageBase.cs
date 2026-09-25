using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor,
		object
	>;

	namespace UI.Client
	{
		public abstract class TerminalPageBase : ITerminalPage
		{
			public string Name
			{

				get { return GetOrSetMemberFunc(null, (int)TerminalPageAccessors.Name) as string; }

				set { GetOrSetMemberFunc(value, (int)TerminalPageAccessors.Name); }
			}

			public object ID => data.Item2;

			public bool Enabled
			{

				get { return (bool)GetOrSetMemberFunc(null, (int)TerminalPageAccessors.Enabled); }

				set { GetOrSetMemberFunc(value, (int)TerminalPageAccessors.Enabled); }
			}

			protected ApiMemberAccessor GetOrSetMemberFunc => data.Item1;

			protected readonly ControlMembers data;


			public TerminalPageBase(ModPages pageEnum)
			{
				data = RichHudTerminal.Instance.GetNewMenuPage(pageEnum);
			}


			public TerminalPageBase(ControlMembers data)
			{
				this.data = data;
			}


			public ControlMembers GetApiData() =>
				data;
		}
	}
}
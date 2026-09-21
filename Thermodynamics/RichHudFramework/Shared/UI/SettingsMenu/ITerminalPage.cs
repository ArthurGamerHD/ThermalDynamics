using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor, // GetOrSetMember
		object // ID
	>;

	namespace UI
	{
		public enum TerminalPageAccessors : int
		{
			Name = 1,

			Enabled = 2,
		}

		public interface ITerminalPage : IModRootMember
		{
/// <summary>Returns the apidata.</summary>
			ControlMembers GetApiData();
		}
	}
}
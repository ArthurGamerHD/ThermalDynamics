using Sandbox.ModAPI;
using System;
using System.IO;
using VRage.Game;

namespace RichHudFramework.IO
{
	public class ReadOnlyModFileIO
	{
		public bool FileExists => MyAPIGateway.Utilities.FileExistsInModLocation(file, mod);

		public readonly string file;

		public readonly MyObjectBuilder_Checkpoint.ModItem mod;


		public ReadOnlyModFileIO(string file, MyObjectBuilder_Checkpoint.ModItem mod)
		{
			this.file = file;
			this.mod = mod;
		}


		public KnownException TryRead(out byte[] stream)
		{
			KnownException exception = null;
			BinaryReader reader = null;

			try
			{
				reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(file, mod);
				stream = reader.ReadBytes(reader.ReadInt32());
			}
			catch (Exception e)
			{
				stream = null;

				exception = new KnownException($"IO Error. Unable to read from {file}.", e);
			}
			finally
			{
				reader?.Close();
			}

			return exception;
		}


		public KnownException TryRead(out string data)
		{
			KnownException exception = null;
			TextReader reader = null;
			data = null;

			try
			{
				reader = MyAPIGateway.Utilities.ReadFileInModLocation(file, mod);
				data = reader.ReadToEnd();
			}
			catch (Exception e)
			{
				data = null;

				exception = new KnownException($"IO Error. Unable to read from {file}.", e);
			}
			finally
			{
				reader?.Close();
			}

			return exception;
		}
	}
}
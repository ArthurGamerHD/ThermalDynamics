using Sandbox.ModAPI;
using System;
using System.IO;

namespace RichHudFramework.IO
{
	public class LocalFileIO
	{
		public bool FileExists => MyAPIGateway.Utilities.FileExistsInLocalStorage(file, typeof(LocalFileIO));

		public readonly string file;

		private readonly object fileLock;


		public LocalFileIO(string file)
		{
			this.file = file;

			fileLock = new object();
		}


		public KnownException TryDuplicate(string newName)
		{
			string data;

			KnownException exception = TryRead(out data);
			LocalFileIO newFile;

			if (exception == null && data != null)
			{

				newFile = new LocalFileIO(newName);
				exception = newFile.TryWrite(data);
			}

			return exception;
		}


		public KnownException TryAppend(string data)
		{
			string current;

			KnownException exception = TryRead(out current);

			if (exception == null && current != null)
			{
				current += data;

				exception = TryWrite(current);
			}
			else

				exception = TryWrite(data);

			return exception;
		}


		public KnownException TryRead(out byte[] stream)
		{
			KnownException exception = null;
			BinaryReader reader = null;

			lock (fileLock)
			{
				try
				{
					reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(file, typeof(LocalFileIO));
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
			}

			return exception;
		}


		public KnownException TryRead(out string data)
		{
			KnownException exception = null;
			TextReader reader = null;
			data = null;

			lock (fileLock)
			{
				try
				{
					reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(file, typeof(LocalFileIO));
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
			}

			return exception;
		}


		public KnownException TryWrite(byte[] stream)
		{
			KnownException exception = null;
			BinaryWriter writer = null;

			lock (fileLock)
			{
				try
				{
					writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(file, typeof(LocalFileIO));
					writer.Write(stream.Length);
					writer.Write(stream);
					writer.Flush();
				}
				catch (Exception e)
				{

					exception = new KnownException($"IO Error. Unable to write to {file}.", e);
				}
				finally
				{
					writer?.Close();
				}
			}

			return exception;
		}


		public KnownException TryWrite(string data)
		{
			KnownException exception = null;
			TextWriter writer = null;

			lock (fileLock)
			{
				try
				{
					writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(file, typeof(LocalFileIO));
					writer.Write(data);
					writer.Flush();
				}
				catch (Exception e)
				{

					exception = new KnownException($"IO Error. Unable to write to {file}.", e);
				}
				finally
				{
					writer?.Close();
				}
			}

			return exception;
		}
	}
}
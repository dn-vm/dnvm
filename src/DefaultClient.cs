using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Dnvm
{
	internal class DefaultClient : IClient
	{
		static HttpClient s_noRedirectClient = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false });
		static HttpClient s_defaultClient = new HttpClient();
		public async Task DownloadArchiveAndExtractAsync(Uri uri, string extractPath)
		{
			var rawStream = await s_defaultClient.GetStreamAsync(uri);
			if (!uri.Segments[^1].EndsWith(".zip"))
				ExtractTarGz(rawStream, extractPath);
			else
				await ExtractZip(rawStream, extractPath);
		}


		/// <summary>
		/// Extracts a <i>.tar.gz</i> archive stream to the specified directory.
		/// </summary>
		/// <param name="stream">The <i>.tar.gz</i> to decompress and extract.</param>
		/// <param name="outputDir">Output directory to write the files.</param>
		public static void ExtractTarGz(Stream stream, string outputDir)
		{
			// A GZipStream is not seekable, so copy it first to a MemoryStream
			using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				const int chunk = 4096;
				using (var memStr = new MemoryStream())
				{
					int read;
					var buffer = new byte[chunk];
					do
					{
						read = gzip.Read(buffer, 0, chunk);
						memStr.Write(buffer, 0, read);
					} while (read == chunk);

					memStr.Seek(0, SeekOrigin.Begin);
					ExtractTar(memStr, outputDir);
				}
			}
		}

		/// <summary>
		/// Extractes a <c>tar</c> archive to the specified directory.
		/// </summary>
		/// <param name="filename">The <i>.tar</i> to extract.</param>
		/// <param name="outputDir">Output directory to write the files.</param>
		public static void ExtractTar(string filename, string outputDir)
		{
			using (var stream = File.OpenRead(filename))
				ExtractTar(stream, outputDir);
		}

		/// <summary>
		/// Extractes a <c>tar</c> archive to the specified directory.
		/// </summary>
		/// <param name="stream">The <i>.tar</i> to extract.</param>
		/// <param name="outputDir">Output directory to write the files.</param>
		public static void ExtractTar(Stream stream, string outputDir)
		{
			var buffer = new byte[100];
			while (true)
			{
				stream.Read(buffer, 0, 100);
				var name = Encoding.ASCII.GetString(buffer).Trim('\0');
				if (String.IsNullOrWhiteSpace(name))
					break;
				stream.Seek(24, SeekOrigin.Current);
				stream.Read(buffer, 0, 12);
				var size = Convert.ToInt64(Encoding.UTF8.GetString(buffer, 0, 12).Trim('\0').Trim(), 8);

				stream.Seek(376L, SeekOrigin.Current);

				var output = Path.Combine(outputDir, name);
				Console.WriteLine(output);
				if (!Directory.Exists(Path.GetDirectoryName(output)))
					Directory.CreateDirectory(Path.GetDirectoryName(output));
				if (!Path.EndsInDirectorySeparator(name))
				{
					using (var str = File.Open(output, FileMode.OpenOrCreate, FileAccess.Write))
					{
						var buf = new byte[size];
						stream.Read(buf, 0, buf.Length);
						str.Write(buf, 0, buf.Length);
					}
				}
				else
				{
					var buf = new byte[size];
					stream.Read(buf, 0, buf.Length);
				}

				var pos = stream.Position;

				var offset = 512 - (pos % 512);
				if (offset == 512)
					offset = 0;

				stream.Seek(offset, SeekOrigin.Current);
			}
		}


		static async Task ExtractTrGz(Stream stream, string extractPath)
		{
			List<Task> extractions = new();
			using (GZipInputStream gZipInputStream = new(stream))
			using (TarReader tarReader = new(gZipInputStream))
			{
				while (await tarReader.GetNextEntryAsync(true) is TarEntry next)
				{
					string filePath = Path.GetFullPath(Path.Combine(extractPath, next.Name));
					if (Path.EndsInDirectorySeparator(filePath))
					{
						Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
						continue;
					}

					extractions.Add(next.ExtractToFileAsync(filePath, true));
				}
				await Task.WhenAll(extractions);
			}
			return;
		}

		static async Task ExtractZip(Stream stream, string extractPath)
		{
			ZipArchive za = new(stream);
			za.ExtractToDirectory(extractPath);
			return;
			using (ZipInputStream zipInputStream = new ZipInputStream(stream))
			{
				int bufferSize = 1024 * 1024;// 81920;
				byte[] buffer = new byte[bufferSize];
				while (zipInputStream.GetNextEntry() is ZipEntry entry)
				{
					if (!entry.IsFile)
						continue;
					string filePath = Path.GetFullPath(Path.Combine(extractPath, entry.Name));
					Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
					long fileSize = entry.Size;
					int totalBytesRead = 0;
					using (FileStream extractedFile = File.Create(filePath, bufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous))
						do
						{
							int bytesLeft = (int)(fileSize - totalBytesRead);
							int bytesToRead = bytesLeft > bufferSize ? bufferSize : bytesLeft;
							int bytesBuffered = await zipInputStream.ReadAsync(buffer, 0, bytesToRead);
							totalBytesRead += bytesBuffered;
							extractedFile.Write(buffer, 0, bytesBuffered);
						} while (totalBytesRead < fileSize);
				}
				return;
			}
		}

		public async Task<HttpResponseMessage> GetHeadersAsync(Uri uri, bool followRedirects = false)
		{
			HttpResponseMessage? response = null;
			int redirects = followRedirects ? 10 : 1;
			for (int i = 0; i < redirects; i++)
			{
				var requestMessage = new HttpRequestMessage(
					HttpMethod.Head,
					uri);
				response = await s_noRedirectClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
				if (response.StatusCode != HttpStatusCode.MovedPermanently)
					break;
				uri = response.Headers.Location!;
			}
			return response!;
		}

		public Task<string> GetStringAsync(Uri uri)
			=> s_defaultClient.GetStringAsync(uri);
	}
}

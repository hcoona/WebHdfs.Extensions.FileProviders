using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;

namespace WebHdfs.Extensions.FileProviders
{
    public class WebHdfsFileInfo : IFileInfo, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly UriBuilder fileWebHdfsUriBuilder;

        public WebHdfsFileInfo(Uri nameNodeUri, string relativePath)
        {
            this.NameNodeUri = nameNodeUri;
            this.RelativePath = relativePath;
            this.httpClient = new HttpClient();
            this.fileWebHdfsUriBuilder = new UriBuilder(new Uri(NameNodeUri, $"/webhdfs/v1/{RelativePath.Trim('/')}"));

            try
            {
                var statusObj = GetFileStatus().Result;
                this.Exists = true;
                this.Length = statusObj.FileStatus.length;
                this.LastModified = FromUnixTimeMilliseconds((long)statusObj.FileStatus.modificationTime);
                this.IsDirectory = string.Compare((string)statusObj.FileStatus.type, "DIRECTORY", StringComparison.CurrentCultureIgnoreCase) == 0;
            }
            catch (AggregateException ex) when(ex.InnerException is FileNotFoundException)
            {
                this.Exists = false;
                this.Length = 0;
                this.LastModified = new DateTimeOffset();
                this.IsDirectory = false;
            }
        }
        
        private DateTimeOffset FromUnixTimeMilliseconds(long milliseconds)
        {
#if _NET462 || _NETSTANDARD2_0
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
#else
            // Number of days in a non-leap year
            const int DaysPerYear = 365;
            // Number of days in 4 years
            const int DaysPer4Years = DaysPerYear * 4 + 1;       // 1461
            // Number of days in 100 years
            const int DaysPer100Years = DaysPer4Years * 25 - 1;  // 36524
            // Number of days in 400 years
            const int DaysPer400Years = DaysPer100Years * 4 + 1; // 146097

            const int DaysTo1970 = DaysPer400Years * 4 + DaysPer100Years * 3 + DaysPer4Years * 17 + DaysPerYear; // 719,162

            const long UnixEpochTicks = TimeSpan.TicksPerDay * DaysTo1970; // 621,355,968,000,000,000
            const long UnixEpochMilliseconds = UnixEpochTicks / TimeSpan.TicksPerMillisecond; // 62,135,596,800,000

            long MinMilliseconds = DateTime.MinValue.Ticks / TimeSpan.TicksPerMillisecond - UnixEpochMilliseconds;
            long MaxMilliseconds = DateTime.MaxValue.Ticks / TimeSpan.TicksPerMillisecond - UnixEpochMilliseconds;

            if (milliseconds < MinMilliseconds || milliseconds > MaxMilliseconds)
            {
                throw new ArgumentOutOfRangeException("milliseconds",
                    string.Format("Valid value between {0} and {1} (included).", MinMilliseconds, MaxMilliseconds));
            }

            long ticks = milliseconds * TimeSpan.TicksPerMillisecond + UnixEpochTicks;
            return new DateTimeOffset(ticks, TimeSpan.Zero);
#endif
        }

        public Uri NameNodeUri { get; }

        public string RelativePath { get; }

        private async Task<dynamic> GetFileStatus()
        {
            fileWebHdfsUriBuilder.Query = "OP=GETFILESTATUS";
            var response = await httpClient.GetAsync(fileWebHdfsUriBuilder.Uri);

            var responseContentObject = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());
            if (response.IsSuccessStatusCode)
            {
                return responseContentObject;
            }
            else
            {
                string message = responseContentObject.RemoteException.message;
                switch (response.StatusCode)
                {
                    case HttpStatusCode.BadRequest: throw new ArgumentException(message);
                    case HttpStatusCode.Unauthorized: throw new System.Security.SecurityException(message);
                    case HttpStatusCode.Forbidden: throw new IOException(message);
                    case HttpStatusCode.NotFound: throw new FileNotFoundException(message, Name);
                    case HttpStatusCode.InternalServerError: throw new InvalidOperationException(message);
                    default: throw new InvalidOperationException(message);
                }
            }
        }

        public bool Exists { get; }

        public long Length { get; }

        public string PhysicalPath => null;

        public string Name => Path.GetFileName(RelativePath);

        public DateTimeOffset LastModified { get; }

        public bool IsDirectory { get; }

        public Stream CreateReadStream()
        {
            if (IsDirectory)
            {
                throw new InvalidOperationException("You cannot create read stream against a directory.");
            }

            fileWebHdfsUriBuilder.Query = "OP=OPEN";
            return httpClient.GetStreamAsync(fileWebHdfsUriBuilder.Uri).Result;
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}

using System;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace WebHdfs.Extensions.FileProviders
{
    public class WebHdfsFileProvider : IFileProvider
    {
        public WebHdfsFileProvider(Uri nameNodeUri)
        {
            this.NameNodeUri = nameNodeUri;
        }

        public Uri NameNodeUri { get; }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            var directoryInfo = new WebHdfsFileInfo(NameNodeUri, subpath);
            if (directoryInfo.Exists && directoryInfo.IsDirectory)
            {
                return new WebHdfsDirectoryContents(directoryInfo);
            }
            else
            {
                return NotFoundDirectoryContents.Singleton;
            }
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return new WebHdfsFileInfo(NameNodeUri, subpath);
        }

        public IChangeToken Watch(string filter)
        {
            throw new NotImplementedException();
        }
    }
}

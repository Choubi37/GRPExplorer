﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using GRPExplorerLib.Util;

namespace GRPExplorerLib.BigFile
{
    public abstract class BigFile
    {
        protected string fileOrDirectory;

        public abstract FileInfo MetadataFileInfo { get; }

        protected BigFileIO fileIO;
        public BigFileIO FileIO { get { return fileIO; } }
        protected BigFileUtil fileUtil;
        public BigFileUtil FileUtil { get { return fileUtil; } }

        protected BigFileFolder rootFolder;
        public BigFileFolder RootFolder { get { return rootFolder; } }
        protected FileMappingData mappingData;
        public FileMappingData MappingData { get { return mappingData; } }

        public bool IsLoaded { get { return rootFolder != null; } }

        public BigFileHeader FileHeader;
        public BigFileFileCountInfo CountInfo;

        public BigFile(string _fileOrDirectory)
        {
            fileOrDirectory = _fileOrDirectory;

            fileIO = new BigFileIO(MetadataFileInfo);
            fileUtil = new BigFileUtil();
        }

        public abstract void LoadFromDisk();
    }
}

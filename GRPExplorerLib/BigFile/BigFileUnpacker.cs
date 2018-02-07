﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using GRPExplorerLib.Util;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading;

namespace GRPExplorerLib.BigFile
{
    public class BigFileUnpacker
    {
        public struct DiagData
        {
            public float GenerateYetiMetadataFile;
            public float CreateDirectories;
            public float CreateRenamedFileMapping;
            public float WriteUnpackedFiles;

            internal void DebugLog(LogProxy log)
            {
                log.Debug(" > BigFileUnpacker.DiagData Dump: ");
                log.Debug("    GenerateYetiMetadataFile: " + GenerateYetiMetadataFile + "ms");
                log.Debug("           CreateDirectories: " + CreateDirectories + "ms");
                log.Debug("    CreateRenamedFileMapping: " + CreateRenamedFileMapping + "ms");
            }
        }

        private class UnpackThreadInfo
        {
            public DirectoryInfo unpackDir;
            public BigFile bigFile;
            public UnpackedRenamedFileMapping fileMapping;
            public int startIndex;
            public int count;
            public int threadID;
            public Action<UnpackThreadInfo> OnWorkDoneCallback;
            public bool isUnpacking = false;
            public Stopwatch stopwatch = new Stopwatch();
        }

        public const int NUM_THREADED_TASKS = 4;

        private LogProxy log = new LogProxy("BigFileUnpacker");

        private PackedBigFile bigFile;

        private Stopwatch stopwatch = new Stopwatch();

        public DiagData diagData;

        private readonly string formatted_diag_msg = string.Format("     {0,6}   {1,6}   {2,6}   {3,6}", "Thread", "Time", "Index", "Count");

        private UnpackThreadInfo[] unpackThreads = new UnpackThreadInfo[NUM_THREADED_TASKS];
        public bool IsUnpacking
        {
            get
            {
                for (int i = 0; i < NUM_THREADED_TASKS; i++)
                {
                    if (unpackThreads[i].isUnpacking)
                        return true;
                }

                return false;
            }
        }


        public BigFileUnpacker(PackedBigFile _bigFile)
        {
            bigFile = _bigFile;
        }

        public void UnpackBigfile(DirectoryInfo dir)
        {
            log.Info("Unpacking a bigfile to directory: \"" + dir.FullName + "\"");

            if (!dir.Exists)
                Directory.CreateDirectory(dir.FullName);

            //if (dir.GetDirectories().Length != 0 && dir.GetFiles().Length != 0)
            //{
            //    log.Error("Unpacking directory must be empty!");
            //    return;
            //}

            if (!dir.Exists)
            {
                log.Info("Directory does not exist, creating it...");
                Directory.CreateDirectory(dir.FullName);
            }

            GenerateYetiMetadataFile(dir, bigFile);

            DirectoryInfo unpackDir = new DirectoryInfo(dir.FullName + "\\" + BigFileConst.UNPACK_DIR);
            log.Info("Creating unpack dir: " + unpackDir.FullName);
            Directory.CreateDirectory(unpackDir.FullName);

            log.Info("Creating unpacked directories...");
            CreateDirectoriesFromTree(unpackDir, bigFile.RootFolder);

            log.Info("Creating renamed mapping file...");
            UnpackedRenamedFileMapping renamedMapping = CreateRenamedFileMapping(bigFile.RootFolder);
            UnpackedFileKeyMappingFile mappingFile = new UnpackedFileKeyMappingFile(dir);
            mappingFile.SaveMappingData(renamedMapping);
            log.Info("Mapping file saved!");

            stopwatch.Reset();
            stopwatch.Start();

            log.Info("Beginning extract...");

            unpackThreads = new UnpackThreadInfo[NUM_THREADED_TASKS];
            int dividedCount = bigFile.MappingData.FilesList.Length / NUM_THREADED_TASKS;
            int dividedRemainder = bigFile.MappingData.FilesList.Length % NUM_THREADED_TASKS;
            log.Info("Divided files into " + NUM_THREADED_TASKS + " pools of " + dividedCount + " with " + dividedRemainder + " left over (to be tacked onto the last!)");
            for (int i = 0; i < NUM_THREADED_TASKS; i++)
            {
                unpackThreads[i] = new UnpackThreadInfo()
                {
                    unpackDir = unpackDir,
                    bigFile = bigFile,
                    fileMapping = renamedMapping,
                    startIndex = i * dividedCount,
                    count = dividedCount,
                    threadID = i,
                    OnWorkDoneCallback = internal_OnThreadFinished
                };
            }
            unpackThreads[3].count += dividedRemainder; //add the remainder onto the last info

            for (int i = 0; i < NUM_THREADED_TASKS; i++)
            {
                ThreadPool.QueueUserWorkItem(internal_UnpackFiles, unpackThreads[i]);
            }

            diagData.WriteUnpackedFiles = stopwatch.ElapsedMilliseconds;
            stopwatch.Stop();

            log.Info("Extract complete!");

            diagData.DebugLog(log);

            log.Info("Unpack complete!");
        }

        private void internal_UnpackFiles(object state)
        {
            UnpackThreadInfo info = state as UnpackThreadInfo;
            info.isUnpacking = true;
            info.stopwatch.Reset();
            info.stopwatch.Start();

            IOBuffers buffers = new IOBuffers();

            int dataOffset = info.bigFile.FileIO.CalculateDataOffset(ref info.bigFile.FileHeader, ref info.bigFile.CountInfo);
            byte[] buffer = buffers[4];
            using (FileStream fs = File.OpenRead(info.bigFile.MetadataFileInfo.FullName))
            {
                BigFileFile currFile = null;
                for (int i = info.startIndex; i < info.startIndex + info.count; i++)
                {
                    currFile = bigFile.MappingData.FilesList[i];
                    if (string.IsNullOrEmpty(currFile.Name))
                    {
                        log.Error(string.Format("File (key:{0:X8}) does not have a file name!", currFile.FileInfo.Key));
                        continue;
                    }

                    log.Info("Unpacking file " + currFile.Name);
                    info.fileMapping.Map[currFile.FileInfo.Key].DebugLog(log);

                    fs.Seek((uint)dataOffset + (uint)(currFile.FileInfo.Offset * 8), SeekOrigin.Begin);

                    buffer = buffers[4];
                    int fileSize = -1;

                    //here we read the data from the bigfile, and decompress it if we need to
                    if (currFile.FileInfo.ZIP == 0) //switch on compression
                    {
                        fs.Read(buffer, 0, 4); //get the size of the file
                        fileSize = BitConverter.ToInt32(buffer, 0);

                        buffer = buffers[fileSize];

                        fs.Read(buffer, 0, fileSize);
                    }
                    else
                    {
                        fs.Read(buffer, 0, 4); //get the compressed size of the file
                        int compressedSize = BitConverter.ToInt32(buffer, 0);
                        fs.Read(buffer, 0, 4); //get uncompressed file size
                        fileSize = BitConverter.ToInt32(buffer, 0);

                        fs.Read(buffer, 0, 2); //we skip the first 2 bytes because it's a zlib header

                        buffer = buffers[fileSize];
                        using (DeflateStream ds = new DeflateStream(fs, CompressionMode.Decompress, true))
                        {
                            ds.Read(buffer, 0, fileSize);
                        }
                    }

                    string fileName = info.unpackDir.FullName + info.fileMapping.Map[currFile.FileInfo.Key].FileName;

                    //write the read data to the unpacked file
                    using (FileStream newFs = File.Create(fileName))
                    {
                        newFs.Write(buffer, 0, fileSize);
                    }
                }
            }

            log.Info("Unpack thread (ID:" + info.threadID + ") finished work!");
            info.isUnpacking = false;
            info.stopwatch.Stop();

            info.OnWorkDoneCallback.Invoke(info);
        }

        private void internal_OnThreadFinished(UnpackThreadInfo info)
        {
            if (!IsUnpacking)
            {
                log.Info("All unpacking threads finished their work!");
                log.Info(" > Time taken: ");
                log.Info(formatted_diag_msg);
                for (int i = 0; i < NUM_THREADED_TASKS; i++)
                {
                    string str = string.Format("  {0,6}        {1,4}s  {2,6}   {3,6}", i, unpackThreads[i].stopwatch.ElapsedMilliseconds / 1000, unpackThreads[i].startIndex, unpackThreads[i].count);
                    log.Info(str);
                }
            }
        }

        public void GenerateYetiMetadataFile(DirectoryInfo dir, PackedBigFile bigfile)
        {
            stopwatch.Reset();
            stopwatch.Start();
            FileInfo metadataFileInfo = new FileInfo(dir.FullName + "\\" + BigFileConst.METADATA_FILE_NAME);
            using (FileStream fs = File.Create(metadataFileInfo.FullName))
            {
                FileBuffer bytesToWrite = bigfile.FileIO.ReadFileAndFolderMetadataRaw(ref bigFile.FileHeader, ref bigFile.CountInfo);
                fs.Write(bytesToWrite.bytes, 0, bytesToWrite.size);
            }
            stopwatch.Stop();
            diagData.GenerateYetiMetadataFile = stopwatch.ElapsedMilliseconds;
        }

        public void CreateDirectoriesFromTree(DirectoryInfo dir, BigFileFolder rootFolder)
        {
            stopwatch.Reset();
            stopwatch.Start();
            string str = "";
            foreach (KeyValuePair<short, BigFileFolder> kvp in bigFile.RootFolder.FolderMap)
            {
                str = dir + "\\" + kvp.Value.FullDirectoryName;
                if (!Directory.Exists(str))
                {
                    log.Debug("   Creating directory: " + str);
                    Directory.CreateDirectory(str);
                }
            }
            stopwatch.Stop();
            diagData.CreateDirectories = stopwatch.ElapsedMilliseconds;
        }

        public UnpackedRenamedFileMapping CreateRenamedFileMapping(BigFileFolder folder, UnpackedRenamedFileMapping mapping = null, Dictionary<string, int> fileRenameCounts = null)
        {
            bool isFirst = mapping == null;
            if (isFirst)
            {
                mapping = new UnpackedRenamedFileMapping();
                fileRenameCounts = new Dictionary<string, int>();
                stopwatch.Reset();
                stopwatch.Start();
            }

            foreach (BigFileFile file in folder.Files)
            {
                string fullName = file.FullFolderPath + file.Name;

                if (fileRenameCounts.ContainsKey(fullName))
                {
                    fileRenameCounts[fullName]++;
                    fullName += "_" + fileRenameCounts[fullName];
                }
                else
                {
                    fileRenameCounts.Add(fullName, 1);
                }

                log.Debug(file.Name + " remapped to " + fullName);

                UnpackedRenamedFileMapping.RenamedFileMappingData data = new UnpackedRenamedFileMapping.RenamedFileMappingData()
                {
                    Key = file.FileInfo.Key,
                    OriginalName = file.Name,
                    FileName = fullName
                };

                mapping.Map.Add(file.FileInfo.Key, data);
            }

            foreach (BigFileFolder childFolder in folder.SubFolders)
            {
                CreateRenamedFileMapping(childFolder, mapping, fileRenameCounts);
            }

            if (isFirst)
            {
                stopwatch.Stop();
                diagData.CreateRenamedFileMapping = stopwatch.ElapsedMilliseconds;
            }

            return mapping;
        }

        public void UnpackFiles(DirectoryInfo dir, BigFileFolder folder, UnpackedRenamedFileMapping fileMapping)
        {

        }
    }
}

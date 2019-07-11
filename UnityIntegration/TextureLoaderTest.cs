﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GRPExplorerLib.BigFile;
using GRPExplorerLib.BigFile.Files;
using GRPExplorerLib.BigFile.Files.Archetypes;
using GRPExplorerLib.Logging;

namespace UnityIntegration
{
    public class TextureLoaderTest : MonoBehaviour
    {
        static TextureLoaderTest inst;

        [RuntimeInitializeOnLoadMethod]
        static void Load()
        {
            if (inst)
                return;

            inst = new GameObject("TextureLoaderTest").AddComponent<TextureLoaderTest>();

            LogManager.LogInterface = new UnityLogInterface();

            LogManager.GlobalLogFlags = LogFlags.Info | LogFlags.Error;
        }

        public GameObject testPlane;
        public BigFile m_bigFile;
        public string currentFilePath = "";
        public YetiTextureFormat textureType = YetiTextureFormat.RGBA32;
        public TextureFormat ImportAs = TextureFormat.RGBA32;
        public List<string> fileNames = new List<string>();
        public List<BigFileFile> textureMetadataFiles = new List<BigFileFile>();
        public List<BigFileFile> displayedFiles = new List<BigFileFile>();
        public BigFileFile loadedPayload;
        public bool Transparent = false;
        public int ImportCount = 50;
        public int ImportStart = 0;
        public int sel = 0;

        Texture2D texture;

        bool isLoaded = false;

        void Start()
        {
            testPlane = (GameObject)Instantiate(Resources.Load("TextureTester"));

            Matrix4x4 m = Matrix4x4.identity;
            m.SetTRS(new Vector3(35f, 0.4325f, -5f), Quaternion.identity, Vector3.one);
            Debug.Log(m);
        }

        public void LoadBigFile()
        {
            if (isLoaded)
                return;

            isLoaded = true;
            IntegrationUtil.LoadBigFileInBackground
                (currentFilePath,
                (bigFile) =>
                {
                    textureMetadataFiles = bigFile.RootFolder.GetAllFilesOfArchetype<TextureMetadataFileArchetype>();
                    bigFile.FileLoader.LoadFiles(textureMetadataFiles);
                    m_bigFile = bigFile;
                });
        }

        public void ChangeDisplayedTextures()
        {
            List<BigFileFile> imports = new List<BigFileFile>();
            foreach (BigFileFile textureFile in textureMetadataFiles)
            {
                if (textureFile.ArchetypeAs<TextureMetadataFileArchetype>().Format == textureType)
                    imports.Add(textureFile);
            }

            if (ImportStart > imports.Count - 1)
            {
                Debug.LogErrorFormat("ImportStart larger than imports count ({0} > {1})", ImportStart, imports.Count);
                return;
            }

            if (ImportStart + ImportCount > imports.Count)
            {
                Debug.LogErrorFormat("ImportCount larger than imports count ({0} > {1})", ImportStart + ImportCount, imports.Count);
                return;
            }

            sel = 0;
            fileNames.Clear();
            displayedFiles.Clear();
            for (int i = ImportStart; i < ImportStart + ImportCount; i++)
            {
                BigFileFile file = imports[i];
                fileNames.Add(file.Name);
                displayedFiles.Add(file);
            }
        }

        public void LoadTextureFile()
        {
            if (Transparent)
            {
                testPlane.GetComponent<Renderer>().material.ChangeRenderMode(StandardShaderUtils.BlendMode.Transparent);
            }
            else
            {
                testPlane.GetComponent<Renderer>().material.ChangeRenderMode(StandardShaderUtils.BlendMode.Opaque);
            }

            loadedPayload?.Unload();

            BigFileFile curr = displayedFiles[sel];
            TextureMetadataFileArchetype arch = curr.ArchetypeAs<TextureMetadataFileArchetype>();

            loadedPayload = arch.Payload.File;
            List<BigFileFile> list = new List<BigFileFile>
            {
                loadedPayload
            };
            m_bigFile.FileLoader.LoadFiles(list);
            
            texture = new Texture2D(arch.Width, arch.Height, ImportAs, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.LoadRawTextureData(arch.Payload.Data);
            texture.Apply();

            testPlane.GetComponent<Renderer>().material.mainTexture = texture;
        }
    }
}

public static class StandardShaderUtils
{
    public enum BlendMode
    {
        Opaque,
        Cutout,
        Fade,
        Transparent
    }

    public static void ChangeRenderMode(this Material standardShaderMaterial, BlendMode blendMode)
    {
        switch (blendMode)
        {
            case BlendMode.Opaque:
                standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                standardShaderMaterial.SetInt("_ZWrite", 1);
                standardShaderMaterial.DisableKeyword("_ALPHATEST_ON");
                standardShaderMaterial.DisableKeyword("_ALPHABLEND_ON");
                standardShaderMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                standardShaderMaterial.renderQueue = -1;
                break;
            case BlendMode.Cutout:
                standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                standardShaderMaterial.SetInt("_ZWrite", 1);
                standardShaderMaterial.EnableKeyword("_ALPHATEST_ON");
                standardShaderMaterial.DisableKeyword("_ALPHABLEND_ON");
                standardShaderMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                standardShaderMaterial.renderQueue = 2450;
                break;
            case BlendMode.Fade:
                standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                standardShaderMaterial.SetInt("_ZWrite", 0);
                standardShaderMaterial.DisableKeyword("_ALPHATEST_ON");
                standardShaderMaterial.EnableKeyword("_ALPHABLEND_ON");
                standardShaderMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                standardShaderMaterial.renderQueue = 3000;
                break;
            case BlendMode.Transparent:
                standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                standardShaderMaterial.SetInt("_ZWrite", 0);
                standardShaderMaterial.DisableKeyword("_ALPHATEST_ON");
                standardShaderMaterial.DisableKeyword("_ALPHABLEND_ON");
                standardShaderMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                standardShaderMaterial.renderQueue = 3000;
                break;
        }

    }
}
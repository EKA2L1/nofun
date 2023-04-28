/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Nofun.Driver.Graphics;
using Nofun.Util;
using UnityEngine;

using System.Collections.Generic;
using System;

using UnityEngine.UI;
using UnityEngine.Rendering;
using Nofun.Module.VMGP3D;

namespace Nofun.Driver.Unity.Graphics
{
    public class GraphicDriver : MonoBehaviour, IGraphicDriver
    {
        private readonly int NGAGE_PPI = 130;
        private const int MEASURED_CACHE_LIMIT = 4000;
        private const string BlackTransparentUniformName = "_Black_Transparent";
        private const string MainTexUniformName = "_MainTex";
        private const string TexcoordTransformMatrixUniformName = "_TexToLocal";
        private const string ColorUniformName = "_Color";

        private const string UnlitZTestUniformName = "_ZTest";
        private const string UnlitCullUniformName = "_Cull";

        private const int TMPSpawnPerAlloc = 5;

        private RenderTexture screenTextureBackBuffer;

        private bool began = false;
        private Action stopProcessor;
        private Rect scissorRect;
        private Rect viewportRect;
        private Texture2D whiteTexture;
        private Vector2 screenSize;
        private float frameTimePassed = -1.0f;
        private Dictionary<string, int> measuredCache;

        private TMPro.FontStyles selectedFontStyles;
        private float selectedFontSize = 11.5f;
        private float selectedOutlineWidth = 0.0f;
        private int fontMeshUsed = 0;

        private CompareFunction depthCompareFunction = CompareFunction.LessEqual;
        private CullMode cullMode = CullMode.Back;
        private bool fixedStateChanged = true;

        private Matrix4x4 projectionMatrix3D = Matrix4x4.identity;
        private Matrix4x4 viewMatrix3D = Matrix4x4.identity;
        private Matrix4x4 orthoMatrix = Matrix4x4.identity;

        private bool in3DMode = false;
        private Texture activeTexture = null;

        private BillboardCache billboardCache;
        private MeshCache meshCache;

        [SerializeField]
        private TMPro.TMP_Text[] textRenders;

        [SerializeField]
        private TMPro.TMP_Text textMeasure;

        [SerializeField]
        private UnityEngine.UI.RawImage displayImage;

        [SerializeField]
        private Material mophunDrawTextureMaterial;

        [SerializeField]
        private Material mophunUnlitMaterial;

        [SerializeField]
        private Camera mophunCamera;

        private CommandBuffer commandBuffer;
        private Mesh quadMesh;

        private List<TMPro.TMP_Text> textRenderInternals;
        private Dictionary<ulong, Material> unlitMaterialCache;

        private Material currentUnlitMaterial;

        [HideInInspector]
        public float FpsLimit { get; set; }

        private float SecondPerFrame => 1.0f / FpsLimit;

        private ulong BuildStateIdentifier()
        {
            return ((ulong)cullMode & 0b11) | ((ulong)depthCompareFunction & 0b1111) << 2;
        }

        private void PrepareUnlitMaterial()
        {
            if (fixedStateChanged)
            {
                ulong stateIdentifier = BuildStateIdentifier();
                if (!unlitMaterialCache.ContainsKey(stateIdentifier))
                {
                    Material mat = new Material(mophunUnlitMaterial);
                    mat.SetFloat(UnlitCullUniformName, (float)cullMode);
                    mat.SetFloat(UnlitZTestUniformName, (float)depthCompareFunction);

                    unlitMaterialCache.Add(stateIdentifier, mat);
                }
                currentUnlitMaterial = unlitMaterialCache[stateIdentifier];
            }
        }

        public Action StopProcessorAction
        {
            set => stopProcessor = value;
        }

        private void SetupWhiteTexture()
        {
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        private void SetupQuadMesh()
        {
            quadMesh = new Mesh();

            Vector3[] vertices = new Vector3[4]
            {
                new Vector3(0, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0),
                new Vector3(1, 0, 0)
            };

            quadMesh.vertices = vertices;

            int[] indicies =
            {
                0, 1, 2,
                0, 2, 3
            };

            quadMesh.triangles = indicies;

            Vector2[] uv = new Vector2[4]
            {
                new Vector2(0, 1),
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1)
            };

            quadMesh.uv = uv;
        }

        public void Initialize(Vector2 size)
        {
            screenTextureBackBuffer = new RenderTexture((int)size.x, (int)size.y, 32);
            scissorRect = new Rect(0, 0, size.x, size.y);
            viewportRect = new Rect(0, 0, size.x, size.y);

            billboardCache = new();
            meshCache = new();
            unlitMaterialCache = new();
            measuredCache = new();

            SetupWhiteTexture();
            SetupQuadMesh();

            displayImage.texture = screenTextureBackBuffer;
            textRenderInternals = new(textRenders);

            this.screenSize = size;

            orthoMatrix = Matrix4x4.Ortho(0, ScreenWidth, 0, ScreenHeight, 1, -100);
        }

        private void Start()
        {
            Initialize(new Vector2(176, 208));
        }

        private Rect GetUnityScreenRect(Rect curRect)
        {
            return new Rect(curRect.x, screenSize.y - (curRect.y + curRect.height), curRect.width, curRect.height);
        }

        private Vector2 GetUnityCoords(float x, float y)
        {
            return new Vector2(x, screenSize.y - y);
        }

        private void DrawTexture(ITexture tex, Rect destRect, Rect sourceRect, float centerX, float centerY, float rotation, SColor color, bool blackIsTransparent)
        {
            DrawTexture(((Texture)tex).NativeTexture, destRect, sourceRect, centerX, centerY, rotation, color, blackIsTransparent);
        }

        private void DrawTexture(Texture2D tex, Rect destRect, Rect sourceRect, float centerX, float centerY, float rotation, SColor color, bool blackIsTransparent)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                var coordMatrix = Matrix4x4.TRS(new Vector3(sourceRect.x, sourceRect.y, 0.0f), Quaternion.identity, new Vector3(sourceRect.width, sourceRect.height));

                Matrix4x4 drawMatrix;

                if (rotation == 0.0f)
                {
                    drawMatrix = Matrix4x4.TRS(new Vector3(destRect.x - centerX, ScreenHeight - (destRect.y - centerY + destRect.height), 0),
                        Quaternion.identity, new Vector3(destRect.width, destRect.height, 0));
                }
                else
                {
                    drawMatrix = Matrix4x4.Translate(new Vector3(destRect.x - centerX, ScreenHeight - (destRect.y - centerY + destRect.height), 0));
                    drawMatrix *= Matrix4x4.TRS(new Vector3(centerX, -centerY), Quaternion.AngleAxis(rotation, Vector3.forward), new Vector3(destRect.width, destRect.height, 0));
                    drawMatrix = Matrix4x4.Translate(new Vector3(-centerX, centerY));
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetTexture(MainTexUniformName, tex);
                block.SetFloat(BlackTransparentUniformName, blackIsTransparent ? 1.0f : 0.0f);
                block.SetMatrix(TexcoordTransformMatrixUniformName, coordMatrix);
                block.SetColor(ColorUniformName, color.ToUnityColor());

                commandBuffer.DrawMesh(quadMesh, drawMatrix, mophunDrawTextureMaterial, 0, 0, block);
            });
        }

        private void UpdateRenderMode()
        {
            if (in3DMode)
            {
                commandBuffer.SetProjectionMatrix(projectionMatrix3D);
                commandBuffer.SetViewMatrix(viewMatrix3D);
            }
            else
            {
                commandBuffer.SetViewMatrix(Matrix4x4.identity);
                commandBuffer.SetProjectionMatrix(orthoMatrix);
            }
        }

        private void BeginRender(bool mode2D = true)
        {
            if (began)
            {
                if (mode2D != !in3DMode)
                {
                    in3DMode = !mode2D;
                    UpdateRenderMode();
                }

                PrepareUnlitMaterial();
                return;
            }

            if (commandBuffer == null)
            {
                commandBuffer = new CommandBuffer();
                commandBuffer.name = "Mophun render buffer";
            }

            if (mophunCamera.renderingPath == RenderingPath.DeferredShading)
            {
                mophunCamera.RemoveCommandBuffer(CameraEvent.AfterGBuffer, commandBuffer);
            }
            else
            {
                mophunCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, commandBuffer);
            }

            commandBuffer.Clear();

            commandBuffer.SetRenderTarget(screenTextureBackBuffer);
            commandBuffer.SetViewport(GetUnityScreenRect(viewportRect));
            
            if (scissorRect.size != Vector2.zero)
            {
                commandBuffer.EnableScissorRect(GetUnityScreenRect(scissorRect));
            }

            began = true;

            UpdateRenderMode();
            PrepareUnlitMaterial();
        }

        public void EndFrame()
        {
        }

        public void ClearScreen(SColor color)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                BeginRender();
                commandBuffer.ClearRenderTarget(false, true, color.ToUnityColor());
            });
        }

        public void ClearDepth(float value)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                BeginRender();

                commandBuffer.ClearRenderTarget(true, false, new Color(), value);
            });
        }

        public ITexture CreateTexture(byte[] data, int width, int height, int mipCount, Driver.Graphics.TextureFormat format, Memory<SColor> palettes = new Memory<SColor>())
        {
            ITexture result = null;

            JobScheduler.Instance.RunOnUnityThreadSync(() =>
            {
                result = new Texture(data, width, height, mipCount, format, palettes);
            });

            return result;
        }

        public void DrawText(int posX, int posY, int sizeX, int sizeY, List<int> positions, ITexture atlas, TextDirection direction, SColor textColor)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                BeginRender();

                if (positions.Count % 2 != 0)
                {
                    throw new ArgumentException("The list of bound values are not aligned by 2!");
                }

                // Just draw them one by one, verticies this small should not be instancing.
                // Batching them would be cool though :)
                int advX = (direction == TextDirection.Horizontal) ? sizeX : 0;
                int advY = (direction == TextDirection.VerticalUp) ? -sizeY : (direction == TextDirection.VerticalDown) ? sizeY : 0;

                Texture2D nativeTex = ((Texture)atlas).NativeTexture;

                float sizeXNormed = (float)sizeX / nativeTex.width;
                float sizeYNormed = (float)sizeY / nativeTex.height;

                for (int i = 0; i < positions.Count; i += 2)
                {
                    Rect destRect = new Rect(posX, posY, sizeX, sizeY);
                    Rect sourceRect = new Rect((float)positions[i] / nativeTex.width, (float)positions[i + 1] / nativeTex.height, sizeXNormed, sizeYNormed);

                    DrawTexture(nativeTex, destRect, sourceRect, 0, 0, 0, textColor, false);

                    posX += advX;
                    posY += advY;
                }
            });
        }

        public void DrawTexture(int posX, int posY, int centerX, int centerY, int rotation, ITexture texture,
            int sourceX = -1, int sourceY = -1, int width = -1, int height = -1, bool blackIsTransparent = false)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                BeginRender();

                int widthToUse = (width == -1) ? texture.Width : width;
                int heightToUse = (height == -1) ? texture.Height : height;

                Rect destRect = new Rect(posX, posY, widthToUse, heightToUse);
                Rect sourceRect = new Rect(0, 0, 1, 1);

                if ((sourceX != -1) && (sourceY != -1))
                {
                    sourceRect = new Rect((float)sourceX / texture.Width, (float)sourceY / texture.Height, (float)widthToUse / texture.Width, (float)heightToUse / texture.Height);
                }

                DrawTexture(texture, destRect, sourceRect, centerX, centerY, rotation, new SColor(1, 1, 1), blackIsTransparent);
            });
        }

        public void FillRect(int x0, int y0, int x1, int y1, SColor color)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                Rect destRect = new Rect(x0, y0, x1 - x0, y1 - y0);
                Rect sourceRect = new Rect(0, 0, 1, 1);

                DrawTexture(whiteTexture, destRect, sourceRect, 0, 0, 0, color, false);
            });
        }

        public void FlipScreen()
        {
            DateTime flipStart = DateTime.Now;

            JobScheduler.Instance.RunOnUnityThreadSync(() =>
            {
                frameTimePassed = 0.0f;

                if (mophunCamera.renderingPath == RenderingPath.DeferredShading)
                {
                    mophunCamera.AddCommandBuffer(CameraEvent.AfterGBuffer, commandBuffer);
                }
                else
                {
                    mophunCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, commandBuffer);
                }

                if (began)
                {
                    began = false;
                    fontMeshUsed = 0;
                }
            });

            DateTime now = DateTime.Now;
            double remaining = SecondPerFrame - (now - flipStart).TotalSeconds;

            if (remaining > 0.0f)
            {
                // Sleep to keep up with the predefined FPS
                System.Threading.Thread.Sleep((int)(remaining * 1000));
            }
        }

        public void SetClipRect(int x0, int y0, int x1, int y1)
        {
            Rect previousRect = scissorRect;
            scissorRect = new Rect(x0, y0, x1 - x0, y1 - y0);
        
            if (previousRect != scissorRect)
            {
                Rect copyUnityRect = GetUnityScreenRect(scissorRect);

                JobScheduler.Instance.RunOnUnityThread(() =>
                {
                    if (began)
                    {
                        commandBuffer.EnableScissorRect(copyUnityRect);
                    }
                });
            }
        }

        public void SetViewport(int left, int top, int width, int height)
        {
            Rect previousRect = viewportRect;
            viewportRect = new Rect(left, top, width, height);

            if (previousRect != viewportRect)
            {
                Rect copyUnityRect = GetUnityScreenRect(scissorRect);

                JobScheduler.Instance.RunOnUnityThread(() =>
                {
                    if (began)
                    {
                        commandBuffer.SetViewport(copyUnityRect);
                    }
                });
            }
        }

        public void GetClipRect(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = (int)scissorRect.xMin;
            x1 = (int)scissorRect.xMax;
            y0 = (int)scissorRect.yMin;
            y1 = (int)scissorRect.yMax;
        }

        private float EmulatedPointToPixels(float point)
        {
            return point * NGAGE_PPI / 72.0f;
        }

        public void SelectSystemFont(uint fontSize, uint fontFlags, int charCodeShouldBeInFont)
        {
            // TODO: Use the character hint
            float fontSizeNew = fontSize;

            if (BitUtil.FlagSet(fontSize, SystemFontSize.PixelFlag))
            {
                fontSizeNew = (fontSize & ~(uint)SystemFontSize.PixelFlag);
            }
            else if (BitUtil.FlagSet(fontSize, SystemFontSize.PointFlag))
            {
                fontSizeNew = (fontSize & ~(uint)SystemFontSize.PointFlag);
                fontSizeNew = EmulatedPointToPixels(fontSize);
            }
            else
            {
                switch ((SystemFontSize)fontSize)
                {
                    case SystemFontSize.Large:
                        {
                            fontSizeNew = 22;
                            break;
                        }

                    case SystemFontSize.Normal:
                        {
                            fontSizeNew = 17;
                            break;
                        }

                    case SystemFontSize.Small:
                        {
                            fontSizeNew = 11.5f;
                            break;
                        }
                }
            }

            selectedFontSize = fontSizeNew;

            TMPro.FontStyles styles = TMPro.FontStyles.Normal;

            if (BitUtil.FlagSet(fontFlags, SystemFontStyle.Bold))
            {
                styles |= TMPro.FontStyles.Bold;
            }

            if (BitUtil.FlagSet(fontFlags, SystemFontStyle.Italic))
            {
                styles |= TMPro.FontStyles.Italic;
            }

            if (BitUtil.FlagSet(fontFlags, SystemFontStyle.Underline))
            {
                styles |= TMPro.FontStyles.Underline;
            }

            if (BitUtil.FlagSet(fontFlags, SystemFontStyle.OutlineEffect))
            {
                selectedOutlineWidth = 0.2f;
            }
            else
            {
                selectedOutlineWidth = 0.0f;
            }

            selectedFontStyles = styles;
        }

        public void DrawSystemText(short x0, short y0, string text, SColor backColor, SColor foreColor)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                BeginRender();

                if (textRenderInternals.Count <= fontMeshUsed)
                {
                    // Add 5 more
                    for (int i = 0; i < TMPSpawnPerAlloc; i++)
                    {
                        GameObject newObj = Instantiate(textRenders[0].gameObject, textRenders[0].transform.parent);
                        newObj.name = $"RenderText{i + fontMeshUsed}";

                        textRenderInternals.Add(newObj.GetComponent<TMPro.TMP_Text>());
                    }
                }

                TMPro.TMP_Text textRender = textRenderInternals[fontMeshUsed];

                textRender.text = text;
                textRender.color = foreColor.ToUnityColor();
                textRender.fontStyle = selectedFontStyles;
                textRender.outlineWidth = selectedOutlineWidth;
                textRender.fontSize = selectedFontSize;
                textRender.outlineColor = backColor.ToUnityColor();

                LayoutRebuilder.ForceRebuildLayoutImmediate(textRender.rectTransform);
                textRender.ForceMeshUpdate();

                Matrix4x4 modelMatrix = Matrix4x4.TRS(GetUnityCoords(x0, y0), Quaternion.identity, Vector3.one);

                commandBuffer.DrawMesh(textRender.mesh, modelMatrix, textRender.fontSharedMaterial);
                fontMeshUsed++;
            });
        }

        public int GetStringExtentRelativeToSystemFont(string value)
        {
            if (measuredCache.TryGetValue(value, out int cachedLength))
            {
                return cachedLength;
            }

            if (measuredCache.Count >= MEASURED_CACHE_LIMIT)
            {
                // Purge all and redo
                measuredCache.Clear();
            }

            int resultValue = 0;

            JobScheduler.Instance.RunOnUnityThreadSync(() =>
            {
                textMeasure.fontStyle = selectedFontStyles;
                textMeasure.outlineWidth = selectedOutlineWidth;
                textMeasure.fontSize = selectedFontSize;

                Vector2 size = textMeasure.GetPreferredValues(value);
                if (value == " ")
                {
                    size.x = textMeasure.fontSize / 4.0f;
                }

                resultValue = (ushort)Math.Round(size.x) | ((ushort)Math.Round(size.y) << 16);
            });

            measuredCache.Add(value, resultValue);
            return resultValue;
        }

        private static Vector2 GetPivotMultiplier(BillboardPivot pivot)
        {
            switch (pivot)
            {
                case BillboardPivot.Center:
                    return new Vector2(0.5f, 0.5f);

                case BillboardPivot.TopLeft:
                    return new Vector2(0.0f, 1.0f);

                case BillboardPivot.TopRight:
                    return new Vector2(1.0f, 1.0f);

                case BillboardPivot.BottomLeft:
                    return new Vector2(0.0f, 0.0f);

                case BillboardPivot.BottomRight:
                    return new Vector2(0.0f, 1.0f);

                default:
                    throw new ArgumentException($"Unhandled pivot value: {pivot}");
            }
        }

        private MaterialPropertyBlock UnlitPropertyBlock
        {
            get
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetTexture(MainTexUniformName, activeTexture.NativeTexture);

                return block;
            }
        }

        public void SetActiveTexture(ITexture tex)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                activeTexture = tex as Texture;
            });
        }

        public void DrawBillboard(NativeBillboard billboard)
        {
            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                CullMode previous = cullMode;
                CullModeUnity = CullMode.Back;

                BeginRender(mode2D: false);

                Mesh billboardMesh = billboardCache.GetBillboardMesh(billboard);

                Matrix4x4 modelMatrix = Matrix4x4.Translate(Struct3DToUnity.MophunVector3ToUnity(billboard.position));

                Vector2 pivotV = GetPivotMultiplier((BillboardPivot)billboard.rotationPointFlag);
                Vector2 size = new Vector2(FixedUtil.FixedToFloat(billboard.fixedWidth), FixedUtil.FixedToFloat(billboard.fixedHeight));

                modelMatrix *= Matrix4x4.TRS(pivotV * size, Quaternion.AngleAxis(FixedUtil.FixedToFloat(billboard.rotation), Vector3.forward),
                    size) * Matrix4x4.Translate(-pivotV * size);

                // Billboard is unlit? It seems so. So not much pass
                commandBuffer.DrawMesh(billboardMesh, modelMatrix, currentUnlitMaterial, 0, 0, UnlitPropertyBlock);
                CullModeUnity = previous;
            });
        }

        public void DrawPrimitives(MpMesh meshToDraw)
        {
            uint identifier = meshCache.GetMeshIdentifier(meshToDraw, out Mesh targetMesh);

            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                BeginRender(mode2D: false);
                if (targetMesh == null)
                {
                    targetMesh = meshCache.GetMesh(identifier);
                }

                commandBuffer.DrawMesh(targetMesh, Matrix4x4.identity, currentUnlitMaterial, 0, 0, UnlitPropertyBlock);
            });
        }

        public void Set3DProjectionMatrix(Matrix4x4 matrix)
        {
            projectionMatrix3D = matrix;

            JobScheduler.Instance.RunOnUnityThread(() =>
            {
                if (in3DMode)
                {
                    commandBuffer.SetProjectionMatrix(matrix);
                }
            });
        }

        public void Set3DViewMatrix(Matrix4x4 matrix)
        {
            if (viewMatrix3D != matrix)
            {
                viewMatrix3D = matrix;

                JobScheduler.Instance.RunOnUnityThread(() =>
                {
                    if (in3DMode)
                    {
                        commandBuffer.SetViewMatrix(matrix);
                    }
                });
            }
        }

        private CullMode CullModeUnity
        {
            get => this.cullMode;
            set
            {
                if (cullMode != value)
                {
                    cullMode = value;
                    fixedStateChanged = true;
                }
            }
        }

        public MpCullMode Cull
        {
            set
            {
                JobScheduler.Instance.RunOnUnityThread(() =>
                {
                    CullMode newCullMode = CullMode.Back;
                    switch (value)
                    {
                        case MpCullMode.None:
                            newCullMode = CullMode.Off;
                            break;

                        case MpCullMode.Clockwise:
                            newCullMode = CullMode.Front;
                            break;

                        case MpCullMode.CounterClockwise:
                            newCullMode = CullMode.Back;
                            break;

                        default:
                            throw new ArgumentException($"Invalid cull mode: {value}");
                    }

                    CullModeUnity = newCullMode;
                });
            }
        }

        public MpCompareFunc DepthFunction
        {
            set
            {
                JobScheduler.Instance.RunOnUnityThread(() =>
                {
                    CompareFunction compareFuncNew = CompareFunction.LessEqual;
                    switch (value)
                    {
                        case MpCompareFunc.Never:
                            compareFuncNew = CompareFunction.Never;
                            break;

                        case MpCompareFunc.Less:
                            compareFuncNew = CompareFunction.Less;
                            break;

                        case MpCompareFunc.LessEqual:
                            compareFuncNew = CompareFunction.LessEqual;
                            break;

                        case MpCompareFunc.Equal:
                            compareFuncNew = CompareFunction.Equal;
                            break;

                        case MpCompareFunc.Greater:
                            compareFuncNew = CompareFunction.Greater;
                            break;

                        case MpCompareFunc.GreaterEqual:
                            compareFuncNew = CompareFunction.GreaterEqual;
                            break;

                        case MpCompareFunc.NotEqual:
                            compareFuncNew = CompareFunction.NotEqual;
                            break;

                        case MpCompareFunc.Always:
                            compareFuncNew = CompareFunction.Always;
                            break;

                        default:
                            throw new ArgumentException($"Unknown depth function {value}");
                            break;
                    }

                    if (compareFuncNew != this.depthCompareFunction)
                    {
                        this.depthCompareFunction = compareFuncNew;
                        fixedStateChanged = true;
                    }
                });
            }
        }

        public int ScreenWidth => (int)screenSize.x;

        public int ScreenHeight => (int)screenSize.y;
    } 
}
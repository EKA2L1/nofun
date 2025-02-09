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

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Nofun.Parser;
using Nofun.Util;
using Nofun.PIP2;
using Nofun.Util.Logging;

namespace Nofun.VM
{
    public class VMLoader
    {
        private VMGPExecutable executable;

        public VMLoader(VMGPExecutable executable)
        {
            this.executable = executable;
        }

        private UInt32 EstimatedCodeSectionSize()
        {
            return MemoryUtil.AlignUp(executable.Header.codeSize, VMMemory.DataAlignment);
        }

        private UInt32 EstimatedDataSize()
        {
            return MemoryUtil.AlignUp(executable.Header.dataSize, VMMemory.DataAlignment);
        }

        private UInt32 EstimatedBssSize()
        {
            return MemoryUtil.AlignUp(executable.Header.bssSize, VMMemory.DataAlignment);
        }

        private UInt32 EstimatedDataSectionSize()
        {
            return EstimatedDataSize() + EstimatedBssSize();
        }

        public UInt32 EstimateNeededProgramSize()
        {
            return EstimatedCodeSectionSize() + EstimatedDataSectionSize();
        }

        private PoolData ProcessRelocation(VMGPPoolItem item, Span<byte> codeData, Span<byte> dataSpan, UInt32 codeAddress, UInt32 dataAddress, UInt32 bssAddress)
        {
            if ((item.itemTarget != 1) && (item.itemTarget != 2))
            {
                throw new InvalidOperationException("Unknown segment relocate type!");
            }

            int targetValueSize = (item.poolType == PoolItemType.Swap16Reloc) ? 2 : 4;
            Span<byte> targetValue;

            try
            {
                targetValue = (item.itemTarget == 1) ? codeData.Slice((int)item.targetOffset, targetValueSize) : dataSpan.Slice((int)item.targetOffset, targetValueSize);
            }
            catch (Exception e)
            {
                Logger.Warning(LogClass.Loader, $"Slicing relocation data failed with: {e}");
                return null;
            }

            if (item.poolType == PoolItemType.SectionRelativeReloc)
            {
                UInt32 value = BinaryPrimitives.ReadUInt32LittleEndian(targetValue);

                bool isInCode = false;
                UInt32 valuePool = 0;

                switch (item.metaOffset)
                {
                    case 1:
                        {
                            valuePool = value + codeAddress;
                            isInCode = true;

                            break;
                        }

                    case 2:
                        {
                            valuePool = value + dataAddress;
                            break;
                        }

                    case 4:
                        {
                            valuePool = value + bssAddress;
                            break;
                        }

                    default:
                        {
                            throw new InvalidOperationException("Unknown segment to relocate data value to!");
                        }
                }

                BinaryPrimitives.WriteUInt32LittleEndian(targetValue, valuePool);

                // This is mostly for the translator, in order to detect calls in potential VTable
                return new PoolData(valuePool)
                {
                    IsCodePointerRelocatedInData = isInCode
                };
            }
            else
            {
                // Swap byte order. The byte order given in the executable is little-endian
                // TODO: Not sure what meta offset does in this case
                if (!BitConverter.IsLittleEndian)
                {
                    if (item.poolType == PoolItemType.Swap16Reloc)
                    {
                        UInt16 value = BinaryPrimitives.ReadUInt16LittleEndian(targetValue);
                        BinaryPrimitives.WriteUInt16BigEndian(targetValue, value);
                    }
                    else
                    {
                        UInt32 value = BinaryPrimitives.ReadUInt32LittleEndian(targetValue);
                        BinaryPrimitives.WriteUInt32BigEndian(targetValue, value);
                    }
                }

                return new PoolData(item.metaOffset);
            }
        }

        private PoolData ProcessGenerateSymbol(VMGPPoolItem poolItem, UInt32 codeAddress, UInt32 dataAddress, UInt32 bssAddress, ICallResolver resolver)
        {
            string symbolName = "";
            if (poolItem.poolType == PoolItemType.GlobalSymbol)
            {
                symbolName = executable.GetString(poolItem.metaOffset);
            }

            switch (poolItem.itemTarget)
            {
                case 1:
                    return new PoolData(poolItem.targetOffset + codeAddress, symbolName)
                    {
                        IsInCode = true
                    };

                case 2:
                    return new PoolData(poolItem.targetOffset + dataAddress, symbolName);

                case 4:
                    return new PoolData(poolItem.targetOffset + bssAddress, symbolName);

                default:
                    throw new InvalidProgramException("Unknown pool data produce action: " + poolItem.itemTarget);
            }
        }

        private PoolData ProcessPoolItem(List<VMGPPoolItem> poolItems, List<PoolData> poolDatas, VMGPPoolItem poolItem, Span<byte> codeData, Span<byte> dataSpan, UInt32 codeAddress, UInt32 dataAddress, UInt32 bssAddress, ICallResolver resolver)
        {
            switch (poolItem.poolType)
            {
                case PoolItemType.ImportSymbol:
                    {
                        string value = executable.GetString(poolItem.metaOffset);
                        return new PoolData(resolver?.Resolve(value), value);
                    }

                case PoolItemType.LocalSymbol:
                case PoolItemType.GlobalSymbol:
                    {
                        return ProcessGenerateSymbol(poolItem, codeAddress, dataAddress, bssAddress, resolver);
                    }

                case PoolItemType.SymbolAdd:
                    {
                        if ((poolItem.metaOffset - 1) < poolItems.Count)
                        {
                            return new PoolData((uint)poolDatas[(int)poolItem.metaOffset - 1].ImmediateInteger +
                                                poolItem.targetOffset)
                            {
                                IsInCode = poolDatas[(int)poolItem.metaOffset - 1].IsInCode
                            };
                        }
                        else
                        {
                            return new PoolData((uint)(ProcessPoolItem(poolItems, poolDatas, poolItems[(int)poolItem.metaOffset - 1], codeData,
                                dataSpan, codeAddress, dataAddress, bssAddress, resolver).ImmediateInteger) + poolItem.targetOffset);
                        }
                    }

                case PoolItemType.SectionRelativeReloc:
                case PoolItemType.Swap32Reloc:
                case PoolItemType.Swap16Reloc:
                    {
                        return ProcessRelocation(poolItem, codeData, dataSpan, codeAddress, dataAddress, bssAddress);
                    }

                case PoolItemType.Const32:
                    return new PoolData(poolItem.targetOffset);

                case PoolItemType.End:
                    {
                        return null;
                    }

                default:
                    {
                        throw new InvalidProgramException("Unrecognised segment relocate command!");
                    }
            }
        }

        private List<PoolData> ProcessPoolItems(List<VMGPPoolItem> poolItems, Span<byte> codeData, Span<byte> dataSpan, UInt32 codeAddress, UInt32 dataAddress, UInt32 bssAddress, ICallResolver resolver)
        {
            List<PoolData> poolDatas = new List<PoolData>();

            foreach (VMGPPoolItem poolItem in poolItems)
            {
                PoolData result = ProcessPoolItem(poolItems, poolDatas, poolItem, codeData, dataSpan, codeAddress, dataAddress, bssAddress, resolver);
                if (result == null)
                {
                    result = new PoolData();
                }
                poolDatas.Add(result);
            }

            return poolDatas;
        }

        public List<PoolData> Load(Span<byte> programMemory, UInt32 baseAddress, ICallResolver callResolver)
        {
            UInt32 codeBaseAddress = baseAddress;
            UInt32 dataBaseAddress = baseAddress + EstimatedCodeSectionSize();
            UInt32 bssBaseAddress = dataBaseAddress + EstimatedDataSize();

            Span<byte> codeSpan = programMemory.Slice(0, (int)EstimatedCodeSectionSize());
            Span<byte> dataSpan = programMemory.Slice((int)EstimatedCodeSectionSize(), (int)EstimatedDataSize());
            Span<byte> bssSpan = programMemory.Slice((int)(EstimatedCodeSectionSize() + EstimatedDataSize()), (int)EstimatedBssSize());

            executable.GetCodeSection(codeSpan);
            executable.GetDataSection(dataSpan);

            // Clear the BSS
            bssSpan.Fill(0x00);

            return ProcessPoolItems(executable.PoolItems, codeSpan, dataSpan, codeBaseAddress, dataBaseAddress, bssBaseAddress, callResolver);
        }
    }
}

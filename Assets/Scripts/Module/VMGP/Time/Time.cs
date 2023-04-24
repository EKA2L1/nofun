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

using Nofun.Driver.Time;
using Nofun.VM;

namespace Nofun.Module.VMGP
{
    [Module]
    public partial class VMGP
    {
        [ModuleCall]
        private uint vGetTickCount()
        {
            return system.TimeDriver.GetMilliSecsTickCount();
        }

        [ModuleCall]
        private void vGetTimeDate(VMPtr<VMDateTime> dateTime)
        {
            var result = system.TimeDriver.GetDateTimeDetail(false);
            dateTime.Write(system.Memory, result);
        }

        [ModuleCall]
        private void vGetTimeDateUTC(VMPtr<VMDateTime> dateTime)
        {
            var result = system.TimeDriver.GetDateTimeDetail(true);
            dateTime.Write(system.Memory, result);
        }
    }
}
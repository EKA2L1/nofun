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

using Nofun.Driver.Audio;
using Nofun.Driver.Graphics;
using Nofun.Driver.Input;
using Nofun.Driver.Time;

namespace Nofun.VM
{
    public class VMSystemCreateParameters
    {
        public IGraphicDriver graphicDriver;
        public IInputDriver inputDriver;
        public IAudioDriver audioDriver;
        public ITimeDriver timeDriver;
        public string persistentDataPath;
        public string inputFileName;

        public VMSystemCreateParameters(IGraphicDriver graphicDriver, IInputDriver inputDriver, IAudioDriver audioDriver, ITimeDriver timeDriver,
            string persistentDataPath, string inputFileName = "")
        {
            this.graphicDriver = graphicDriver;
            this.inputDriver = inputDriver;
            this.audioDriver = audioDriver;
            this.timeDriver = timeDriver;
            this.persistentDataPath = persistentDataPath;
            this.inputFileName = inputFileName;
        }
    }
}
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

using System.Collections.Generic;
using System.Linq;

namespace Nofun.Util
{
    public abstract class DictBasedCache<T> : Cache<T> where T : ICacheEntry
    {

        protected Dictionary<uint, T> cache;

        protected override T GetFromCache(uint key)
        {
            if (cache.TryGetValue(key, out var value))
            {
                value.LastAccessed = System.DateTime.Now;
                return value;
            }

            return default;
        }

        protected override void AddToCache(uint key, T entry)
        {
            if (!cache.ContainsKey(key))
            {
                cache.Add(key, entry);
            }
        }

        public DictBasedCache()
        {
            this.cache = new();
        }

        public abstract override void Purge();
    }
}
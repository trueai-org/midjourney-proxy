// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities.
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing.
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited.
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

namespace Midjourney.Base.Util
{
    /// <summary>
    /// Midjourney 禁用词辅助类
    /// </summary>
    public static class MjBannedWordsHelper
    {
        /// <summary>
        /// 禁用词列表
        /// </summary>
        private const string BANNED_WORDS = """
            ahegao
            anus
            ass
            ballgag
            barely dressed
            bloodbath
            bloody
            bodily fluids
            boobs
            breasts
            brothel
            brown pudding
            bruises
            bunghole
            cannibal
            cannibalism
            car crash
            cleavage
            clunge
            cocaine
            coon
            corpse
            crack
            crotch
            crucifixion
            decapitate
            deepfake
            dick
            dominatrix
            erotic
            excrement
            explicit
            fart
            farts
            feces
            flesh
            fuck
            full frontal
            gore
            gory
            gruesome
            guts
            hardcore
            hentai
            heroin
            hitler
            honkey
            incest
            infected
            intimate parts
            jav
            kill
            killing
            kinbaku
            labia
            legs spread
            lingerie
            loli
            lolita
            making love
            mammaries
            massacre
            meth
            naked
            negligee
            nipple
            no clothes
            nude
            organs
            orgy
            ovaries
            penis
            phallus
            porn
            rape
            rear nude
            rule34
            scantily clad
            see-through
            sexual
            sexualized
            shag
            shibari
            shit
            skeletal gore
            slaughter
            slave
            smut
            sperm
            suicide
            tied up
            torture
            unclothed
            vagina
            veiny penis
            violence
            visceral
            vivisection
            vomit
            wang
            warts
            wearing nothing
            wincest
            wound
            zero clothes
            """;

        /// <summary>
        /// 获取禁用词列表
        /// </summary>
        /// <returns></returns>
        public static List<string> GetBannedWords()
        {
            return BANNED_WORDS
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
        }
    }

    /// <summary>
    /// 提示包含禁用词异常
    /// </summary>
    public class BannedPromptException : Exception
    {
        public BannedPromptException(string message)
            : base(message)
        {
        }
    }
}
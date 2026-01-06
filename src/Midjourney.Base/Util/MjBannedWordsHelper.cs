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
            blood
            twerk
            making love
            voluptuous
            naughty
            wincest
            orgy
            no clothes
            au naturel
            no shirt
            decapitate
            bare
            nude
            barely dressed
            nude
            bra
            risque
            scantily clad
            cleavage
            stripped
            infested
            full frontal
            unclothed
            invisible clothes
            wearing nothing
            lingerie
            with no shirt
            naked
            without clothes on
            negligee
            zero clothes
            gruesome
            fascist
            nazi
            prophet mohammed
            slave
            coon
            honkey
            cocaine
            heroin
            meth
            crack
            kill
            belle delphine
            hitler
            jinping
            lolita
            president xi
            torture
            disturbing
            farts
            fart
            poop
            infected
            warts
            shit
            brown pudding
            bunghole
            vomit
            voluptuous
            seductive
            sperm
            sexy
            sadist
            sensored
            censored
            silenced
            deepfake
            inappropriate
            waifu
            succubus
            slaughter
            surgery
            reproduce
            crucified
            seductively
            explicit
            inappropriate
            large bust
            explicit
            wang
            inappropriate
            teratoma
            intimate
            see through
            tryphophobia
            bloodbath
            wound
            cronenberg
            khorne
            cannibal
            cannibalism
            visceral
            guts
            bloodshot
            gory
            killing
            crucifixion
            surgery
            vivisection
            massacre
            hemoglobin
            suicide
            arse
            labia
            ass
            mammaries
            badonkers
            bloody
            minge
            big ass
            mommy milker
            booba
            nipple
            oppai
            booty
            organs
            bosom
            ovaries
            flesh
            breasts
            penis
            busty
            phallus
            clunge
            sexy female
            crotch
            skimpy
            dick
            thick
            bruises
            girth
            titty
            honkers
            vagina
            hooters
            veiny
            knob
            ahegao
            pinup
            ballgag
            car crash
            playboy
            bimbo
            pleasure
            bodily fluids
            pleasures
            boudoir
            rule34
            brothel
            seducing
            dominatrix
            corpse
            seductive
            erotic
            seductive
            fuck
            sensual
            hardcore
            sexy
            hentai
            shag
            horny
            crucified
            shibari
            incest
            smut
            jav
            succubus
            jerk off king at pic
            thot
            kinbaku
            legs spread
            sensuality
            belly button
            porn
            patriotic
            bleed
            excrement
            petite
            seduction
            mccurry
            provocative
            sultry
            erected
            camisole
            tight white
            arrest
            see-through
            feces
            anus
            revealing clothing
            vein
            loli
            -edge
            boobs
            -backed
            tied up
            zedong
            bathing
            jail
            reticulum
            rear end
            sakimichan
            behind bars
            shirtless
            sakimichan
            seductive
            sexi
            sexualiz
            sexual
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
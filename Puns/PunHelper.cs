﻿using System;
using System.Collections.Generic;
using System.Linq;
using MoreLinq;
using Pronunciation;
using WordNet;

namespace Puns
{
    public static class PunHelper
    {
        public static int? GetPunScore(PhoneticsWord originalPhoneticsWord, PhoneticsWord replacementPhoneticsWord)
        {
            if (IsSameWord(originalPhoneticsWord.Text, replacementPhoneticsWord.Text))
                return null;

            var minLength = Math.Min(originalPhoneticsWord.Symbols.Count, replacementPhoneticsWord.Symbols.Count);

            if (minLength < 3)
                return null;


            var longestStreak = 0;
            var currentStreak = 0;
            var unmatchedSymbols = 0;
            var otherMatches = 0;

            void EndStreak()
            {
                if (longestStreak < currentStreak)
                {
                    otherMatches += longestStreak;
                    longestStreak = currentStreak;
                }
                else
                    otherMatches += currentStreak;

                currentStreak = 0;
            }

            for (var i = 0; i < minLength; i++)
            {
                if (originalPhoneticsWord.Symbols[i] == replacementPhoneticsWord.Symbols[i])
                    currentStreak++;
                else if (originalPhoneticsWord.SyllableTypes.Value[i] == replacementPhoneticsWord.SyllableTypes.Value[i])
                {
                    EndStreak();
                    otherMatches++;
                }
                else
                {
                    EndStreak();
                    unmatchedSymbols++;
                }
            }

            EndStreak();

            //now do the same thing going from the back

            for (var i = 0; i < minLength; i++)
            {
                if (originalPhoneticsWord.Symbols[^(1 + i)] == replacementPhoneticsWord.Symbols[^(1 + i)])
                    currentStreak++;
                else if (originalPhoneticsWord.SyllableTypes.Value[^(1 + i)] == replacementPhoneticsWord.SyllableTypes.Value[^(1 + i)])
                {
                    EndStreak();
                    otherMatches++;
                }
                else
                {
                    EndStreak();
                    unmatchedSymbols++;
                }
            }

            EndStreak();



            var score = Triangle(longestStreak) - Triangle(unmatchedSymbols);

            if (score > 2) return score;
            return null;
            static int Triangle(int n) => n * (n + 1) / 2; //returns the nth triangular number
        }

        public static IReadOnlyCollection<Pun> GetPuns(PunCategory category,
            string theme,
            SynSet synSet,
            WordNetEngine wordNetEngine,
            PronunciationEngine pronunciationEngine)
        {
            var phrases = GetPhrases(category);

            var themeWords = GetRelatedWords(theme, synSet, wordNetEngine)
                .Select(x => x.Word).Prepend(theme)

                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(pronunciationEngine.GetPhoneticsWords)
                .Where(x=>x.Symbols.Count > 2)
                .Distinct(WordPronunciationComparer.Instance)

                .ToList();

            var cache = new Dictionary<PhoneticsWord, PhoneticsWord?>();

            var puns = new List<Pun>();

            foreach (var phrase in phrases)
            {
                var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (words.Length == 1)
                    continue;

                foreach (var word in words)
                {
                    var cmuWord = pronunciationEngine.GetPhoneticsWords(word).FirstOrDefault();
                    if (cmuWord is null) continue;
                    if (cmuWord.Symbols.Count < 3) continue;

                    var casing = DetectCasing(word);

                    if (!cache.TryGetValue(cmuWord, out var bestPunWord))
                    {
                        bestPunWord = themeWords.Select(themeWord=> (themeWord, score: GetPunScore(themeWord, cmuWord)))
                            .Where(x=>x.score.HasValue)
                            .OrderByDescending(x=>x.score!.Value)
                            .Select(x=>x.themeWord)
                            .FirstOrDefault();
                        cache.Add(cmuWord, bestPunWord);
                    }

                    if (bestPunWord == null) continue;

                    var newString = ToCase(bestPunWord.Text, casing);

                    var newPhrase = phrase.Replace(word, newString);
                    puns.Add(new Pun(newPhrase, phrase, bestPunWord.Text, synSet));

                }
            }

            return puns;
        }


        public static IReadOnlyCollection<string> GetPhrases(PunCategory category)
        {
            return category switch
            {

                PunCategory.Movies => CategoryLists.Movies.Split("\n",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                PunCategory.Idiom => CategoryLists.Idioms.Split("\n",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                PunCategory.Bands => CategoryLists.Bands.Split("\n",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                PunCategory.Books => CategoryLists.Books.Split("\n",
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
            };
        }


        public static Casing DetectCasing(string s)
        {
            if (s.All(char.IsLower))
                return Casing.Lower;
            if (s.All(char.IsUpper))
                return Casing.Upper;

            return Casing.Title; //Not 100% perfect
        }

        public static string ToCase(string s, Casing casing)
        {
            return casing switch
            {
                Casing.Lower => s.ToLower(),
                Casing.Upper => s.ToUpper(),
                Casing.Title => System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(s.ToLower()),
                _ => throw new ArgumentOutOfRangeException(nameof(casing), casing, null)
            };
        }

        public static bool IsSameWord(string s1, string s2)
        {
            //TODO improve
            return s1.Equals(s2, StringComparison.OrdinalIgnoreCase) ||
                   (s1 + "s").Equals(s2, StringComparison.OrdinalIgnoreCase) ||
                   (s2 + "s").Equals(s1, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly List<SynSetRelation> Relations = new List<SynSetRelation>()
        {
            SynSetRelation.Hyponym,
            SynSetRelation.TopicDomainMember
        };

        public static IEnumerable<RelatedWord> GetRelatedWords(string relatedToWord, SynSet synSet,
            WordNetEngine wordNetEngine)
        {
            var synSets = GetPunSynSets(synSet, wordNetEngine);

            foreach (var set in synSets)
            foreach (var word in set.Words)
                yield return new RelatedWord(word, relatedToWord, "...", set.Gloss);
        }

        public static IEnumerable<SynSet> GetPunSynSets(SynSet synSet, WordNetEngine engine)
        {

            var oneStepSets = synSet.GetRelatedSynSets(SingleStepRelations, false, engine);
            var multiStepSets = synSet.GetRelatedSynSets(RecursiveRelations, true, engine);



            return oneStepSets.Concat(multiStepSets).Prepend(synSet).Distinct();

        }

        /// <summary>
        /// Relations that can be followed recursively
        /// </summary>
        private static readonly IReadOnlySet<SynSetRelation> RecursiveRelations = new HashSet<SynSetRelation>()
        {
            SynSetRelation.Hyponym,
            SynSetRelation.InstanceHyponym,

            SynSetRelation.RegionDomainMember,
            SynSetRelation.TopicDomainMember,
            SynSetRelation.UsageDomainMember,
        };

        /// <summary>
        /// Relations that should only be followed a single step
        /// </summary>
        private static readonly IReadOnlySet<SynSetRelation> SingleStepRelations = new HashSet<SynSetRelation>()
        {
            SynSetRelation.Hypernym,
                SynSetRelation.SimilarTo,

                SynSetRelation.MemberMeronym,
                SynSetRelation.SubstanceHolonym,
                SynSetRelation.PartMeronym,

                SynSetRelation.PartHolonym,
                SynSetRelation.SubstanceHolonym,
                SynSetRelation.MemberHolonym,

                SynSetRelation.RegionDomain,
                SynSetRelation.TopicDomain,
                SynSetRelation.UsageDomain,

                SynSetRelation.AlsoSee,
                SynSetRelation.Cause,
                SynSetRelation.Attribute,
                SynSetRelation.Entailment,
                SynSetRelation.DerivedFromAdjective,
                SynSetRelation.ParticipleOfVerb,
        };
    }
}

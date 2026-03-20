using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace InputSound
{
    internal class CILInjectionPointFinder
    {
        public IList<(OpCode opcode, object operand)> MatchInstructions { get; private set; }
        public int CurrentRepeatCount { get; private set; }
        public bool IsInjected { get; private set; }

        public CILInjectionPointFinder(IList<(OpCode opcode, object operand)> matchInstructions)
        {
            MatchInstructions = matchInstructions;
        }

        public bool IsInjectionPoint(in CodeInstruction instruction)
        {
            if (CurrentRepeatCount >= MatchInstructions.Count())
                return false;

            var current = MatchInstructions[CurrentRepeatCount];
            if (!instruction.Is(current.opcode, current.operand))
            {
                CurrentRepeatCount = 0;
                return false;
            }

            CurrentRepeatCount++;

            if (CurrentRepeatCount != MatchInstructions.Count())
                return false;

            IsInjected = true;
            return true;
        }

        public void Reset()
        {
            CurrentRepeatCount = 0;
            IsInjected = false;
        }
    }
}

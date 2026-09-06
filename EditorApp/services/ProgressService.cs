using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditorApp.services
{
    internal class ProgressService
    {
        private readonly Action<int, string> _set; 
        private readonly List<(string name, int weight)> _steps;
        private readonly int _totalWeight;

        private int _baseProgress;

        public ProgressService(Action<int, string> set, IEnumerable<(string name, int weight)> steps)
        {
            _set = set;
            _steps = steps.ToList();
            _totalWeight = _steps.Sum(step => step.weight);
            _baseProgress = 0;
        }

        public IProgress<(int current, int total)> CreateStepProgress(int stepIndex)
        {
            var step = _steps[stepIndex];
            int stepStart = _baseProgress;
            int stepSpan = (int)Math.Round(100.0 * step.weight / _totalWeight);

            return new Progress<(int current, int total)>(progress =>
            {
                double ratio = progress.total <= 0 ? 0 : (double)progress.current / progress.total;
                int value = (int)Math.Round(stepStart + stepSpan * ratio);
                _set(value, $"{step.name}: {progress.current}/{progress.total}");
            });
        }

        public void CompleteStep(int stepIndex)
        {
            var step = _steps[stepIndex];
            int stepSpan = (int)Math.Round(100.0 * step.weight / _totalWeight);
            _baseProgress += stepSpan;
        }

        public void SetText(string text) => _set(_baseProgress, text);
        public void Finish() => _set(100, "Готово!");
    }
}

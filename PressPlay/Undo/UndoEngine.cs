using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PressPlay.Undo
{
    /// <summary>
    /// Interface for an undo engine that manages undo and redo operations.
    /// </summary>
    public interface IUndoEngine : INotifyPropertyChanged
    {
        event EventHandler<IUndoUnit> OnUndoing;
        event EventHandler<IUndoUnit> OnUndone;
        Stack<IUndoUnit> UndoStack { get; }
        Stack<IUndoUnit> RedoStack { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        void Undo();
        void Redo();
        void AddUndoUnit(IUndoUnit undoUnit);
        void ClearUndoStack();
        void ClearRedoStack();
        void ClearAll();
        void OnPropertyChanged([CallerMemberName] string propertyName = null);
    }

    /// <summary>
    /// An implementation of the undo engine.
    /// </summary>
    public class UndoEngine : IUndoEngine
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<IUndoUnit> OnUndoing;
        public event EventHandler<IUndoUnit> OnUndone;

        public Stack<IUndoUnit> UndoStack { get; } = new Stack<IUndoUnit>();
        public Stack<IUndoUnit> RedoStack { get; } = new Stack<IUndoUnit>();

        public bool CanUndo { get { return UndoStack.Count != 0; } }
        public bool CanRedo { get { return RedoStack.Count != 0; } }

        /// <summary>
        /// A singleton instance of the undo engine.
        /// </summary>
        public static UndoEngine Instance { get; } = new UndoEngine();

        public void Undo()
        {
            if (CanUndo)
            {
                var undoUnit = UndoStack.Pop();
                OnUndoing?.Invoke(this, undoUnit);
                undoUnit.Undo();
                RedoStack.Push(undoUnit);
                OnUndone?.Invoke(this, undoUnit);
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
            }
        }

        public void Redo()
        {
            if (CanRedo)
            {
                var redoUnit = RedoStack.Pop();
                OnUndoing?.Invoke(this, redoUnit);
                redoUnit.Redo();
                UndoStack.Push(redoUnit);
                OnUndone?.Invoke(this, redoUnit);
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
            }
        }

        public void AddUndoUnit(IUndoUnit undoUnit)
        {
            UndoStack.Push(undoUnit);
        }

        public void ClearUndoStack()
        {
            UndoStack.Clear();
        }

        public void ClearRedoStack()
        {
            RedoStack.Clear();
        }

        public void ClearAll()
        {
            ClearUndoStack();
            ClearRedoStack();
        }

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Backrooms.Camcorder
{
    /// <summary>Forwards pointer enter/exit to callbacks (used for the settings hover explanations).</summary>
    public class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action onEnter, onExit;
        public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
        public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
    }
}

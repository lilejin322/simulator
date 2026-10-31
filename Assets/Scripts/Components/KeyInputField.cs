/**
 * Copyright (c) 2021 LG Electronics, Inc.
 *
 * This software contains code licensed as described in LICENSE.
 *
 */

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Simulator.Web
{
    /// <summary>
    /// InputField that only handles key presses. The stock OnUpdateSelected also reads Event.type to handle
    /// "SelectAll" commands, which crashed the Linux player in IMGUI::GetEventType on the loader screen
    /// (seen on a virtual display); Event.rawType does not touch the missing IMGUI state.
    /// </summary>
    public class KeyInputField : InputField
    {
        readonly Event processingEvent = new Event();

        public override void OnUpdateSelected(BaseEventData eventData)
        {
            if (!isFocused)
                return;

            bool consumedEvent = false;
            while (Event.PopEvent(processingEvent))
            {
                if (processingEvent.rawType != EventType.KeyDown)
                    continue;

                consumedEvent = true;
                if (KeyPressed(processingEvent) == EditState.Finish)
                {
                    DeactivateInputField();
                }
            }

            if (consumedEvent)
                UpdateLabel();

            eventData.Use();
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public static class NativeControlsHelp
    {
        private static readonly string[] Titles = { "Поле", "Здания", "Группы", "Карта", "Клавиатура и мышь" };
        private static readonly string[] Images = { "field", "buildings", "groups", "map", "keyboard" };

        public static VisualElement Open(VisualElement root, NativeMenuNavigation navigation, Action closed)
        {
            var page = OrbitalTheme.Screen(root, "controls-help");
            var card = OrbitalTheme.Card(page);
            card.style.width = 1200;
            card.style.maxWidth = Length.Percent(96);
            card.style.maxHeight = Length.Percent(94);
            card.style.height = Length.Percent(94);
            card.style.paddingTop = card.style.paddingBottom = 12;
            card.style.paddingLeft = card.style.paddingRight = 16;
            card.style.minHeight = 0;
            var title = OrbitalTheme.Text("УПРАВЛЕНИЕ", "orbital-title");
            title.style.fontSize = 22;
            title.style.marginBottom = 8;
            card.Add(title);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.flexWrap = Wrap.Wrap;
            card.Add(tabs);

            var scroll = OrbitalTheme.Scroll(ScrollViewMode.Vertical);
            scroll.name = "controls-scroll";
            scroll.focusable = true;
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.minHeight = 0;
            card.Add(scroll);

            var image = new Image { name = "controls-help-image", scaleMode = ScaleMode.ScaleToFit };
            image.style.width = Length.Percent(100);
            image.style.flexShrink = 0;
            image.style.marginTop = 8;
            scroll.Add(image);
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float available = evt.newRect.height;
                if (available > 0) image.style.height = Mathf.Max(220, available - 64);
            });

            var equivalents = OrbitalTheme.Text("", "orbital-muted");
            equivalents.name = "help-controller-equivalents";
            equivalents.style.whiteSpace = WhiteSpace.Normal;
            equivalents.style.marginTop = 8;
            scroll.Add(equivalents);

            var reminders = OrbitalTheme.Text("Лобби: X добавляет бота, Y присоединяет контроллер. В меню нажмите «Назад», чтобы вернуться к матчу или лобби.", "orbital-muted");
            reminders.style.whiteSpace = WhiteSpace.Normal;
            reminders.style.marginTop = 10;
            scroll.Add(reminders);

            var buttons = new Button[Titles.Length];
            void Select(int selected)
            {
                scroll.scrollOffset = Vector2.zero;
                var texture = Resources.Load<Texture2D>("ControlsHelp/" + Images[selected]);
                image.image = texture;
                image.style.display = texture == null ? DisplayStyle.None : DisplayStyle.Flex;
                equivalents.style.display = selected < 4 && texture != null ? DisplayStyle.Flex : DisplayStyle.None;
                reminders.style.display = selected == 0 ? DisplayStyle.Flex : DisplayStyle.None;

                if (texture == null)
                {
                    var error = scroll.Q<Label>("controls-help-image-error");
                    if (error == null)
                    {
                        error = OrbitalTheme.Text("Схема управления недоступна.", "orbital-muted");
                        error.name = "controls-help-image-error";
                        error.style.whiteSpace = WhiteSpace.Normal;
                        scroll.Add(error);
                    }
                    error.style.display = DisplayStyle.Flex;
                }
                else
                {
                    var error = scroll.Q<Label>("controls-help-image-error");
                    if (error != null) error.style.display = DisplayStyle.None;
                }

                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].style.color = i == selected ? OrbitalTheme.Cyan : OrbitalTheme.Ink;
                    buttons[i].style.borderTopColor = i == selected ? OrbitalTheme.Cyan : OrbitalTheme.Line;
                }
                RefreshEquivalents();
            }

            void RefreshEquivalents()
            {
                if (equivalents.style.display == DisplayStyle.None) return;
                var pad = navigation.CurrentGamepad;
                var a = NativeControllerGlyph.Symbol("A", pad);
                var b = NativeControllerGlyph.Symbol("B", pad);
                var x = NativeControllerGlyph.Symbol("X", pad);
                var y = NativeControllerGlyph.Symbol("Y", pad);
                equivalents.text = a == "A" && b == "B" && x == "X" && y == "Y"
                    ? "Кнопки на схемах: A / B / X / Y"
                    : "На схемах — Xbox: A → " + a + " · B → " + b + " · X → " + x + " · Y → " + y;
            }

            for (int i = 0; i < Titles.Length; i++)
            {
                int selected = i;
                buttons[i] = OrbitalTheme.Action(Titles[i], () => Select(selected), "help-tab-" + i);
                buttons[i].style.flexGrow = 1;
                buttons[i].style.flexBasis = 0;
                buttons[i].style.minWidth = 120;
                tabs.Add(buttons[i]);
            }
            Select(0);
            equivalents.schedule.Execute(RefreshEquivalents).Every(250);

            Action back = () => { page.RemoveFromHierarchy(); closed(); };
            var backButton = OrbitalTheme.Action("Назад", back, "controls-back");
            backButton.style.height = 36;
            backButton.style.minHeight = 36;
            card.Add(backButton);
            var hints = OrbitalTheme.Text("", "orbital-hints");
            hints.style.marginTop = 4;
            card.Add(hints);
            navigation.SetScope(page, back, buttons[0], hints);
            return page;
        }
    }
}

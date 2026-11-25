using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SpaceKatMonitor
{
    public partial class MainWindow
    {
        private ButtonLayout _buttonLayout = ButtonLayout.Default;
        private readonly int[] _buttonLayoutOrder = new int[ButtonLayout.Count];
        private readonly string[] _buttonLayoutNames = new string[ButtonLayout.Count];
        private StackPanel _buttonLayoutRows;
        private TextBlock _buttonLayoutStatus;

        private void InitializeButtonLayout()
        {
            _buttonLayoutRows = Find<StackPanel>("ButtonLayoutRows");
            _buttonLayoutStatus = Find<TextBlock>("ButtonLayoutStatus");
            Find<Button>("ButtonLayoutSave").Click += delegate { SaveButtonLayout(); };
            Find<Button>("ButtonLayoutReset").Click += delegate
            {
                StageButtonLayout(ButtonLayout.Default);
                RenderButtonLayoutRows();
                UpdateButtonLayoutStatus();
            };
            LoadButtonLayout();
        }

        private void LoadButtonLayout()
        {
            ButtonLayout loaded = ButtonLayout.Default;
            foreach (string candidate in SettingCandidates(ButtonLayout.FileName))
            {
                ButtonLayout parsed;
                if (ButtonLayout.TryParse(candidate, out parsed)) { loaded = parsed; break; }
            }
            _buttonLayout = loaded;
            StageButtonLayout(loaded);
            RenderButtonLayoutRows();
            _buttonLayoutStatus.Text = "";
            ApplyButtonPresentation();
        }

        private void StageButtonLayout(ButtonLayout layout)
        {
            for (int i = 0; i < ButtonLayout.Count; i++)
            {
                _buttonLayoutOrder[i] = layout.SlotAt(i);
                _buttonLayoutNames[i] = layout.Name(i);
            }
        }

        private void RenderButtonLayoutRows()
        {
            _buttonLayoutRows.Children.Clear();
            for (int position = 0; position < ButtonLayout.Count; position++)
            {
                int slot = _buttonLayoutOrder[position];
                int rowPosition = position;
                Grid row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock label = new TextBlock { Text = "实体按键 " + (slot + 1), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(label);
                TextBox name = new TextBox { Text = _buttonLayoutNames[slot], MaxLength = ButtonLayout.MaxNameLength,
                    Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(7, 0, 7, 0),
                    ToolTip = "留空使用默认名称", Margin = new Thickness(0, 0, 8, 0) };
                AutomationProperties.SetName(name, "实体按键 " + (slot + 1) + " 名称");
                name.TextChanged += delegate { _buttonLayoutNames[slot] = name.Text; UpdateButtonLayoutStatus(); };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                Button up = new Button { Content = "上移", Height = 32, Padding = new Thickness(9, 0, 9, 0),
                    Margin = new Thickness(0, 0, 6, 0), IsEnabled = position > 0 };
                AutomationProperties.SetName(up, "实体按键 " + (slot + 1) + " 上移");
                up.Click += delegate { MoveButtonLayoutRow(rowPosition, -1); };
                Grid.SetColumn(up, 2);
                row.Children.Add(up);
                Button down = new Button { Content = "下移", Height = 32, Padding = new Thickness(9, 0, 9, 0),
                    IsEnabled = position < ButtonLayout.Count - 1 };
                AutomationProperties.SetName(down, "实体按键 " + (slot + 1) + " 下移");
                down.Click += delegate { MoveButtonLayoutRow(rowPosition, 1); };
                Grid.SetColumn(down, 3);
                row.Children.Add(down);
                _buttonLayoutRows.Children.Add(row);
            }
        }

        private void MoveButtonLayoutRow(int position, int direction)
        {
            int target = position + direction;
            if (target < 0 || target >= ButtonLayout.Count) return;
            int slot = _buttonLayoutOrder[position];
            _buttonLayoutOrder[position] = _buttonLayoutOrder[target];
            _buttonLayoutOrder[target] = slot;
            RenderButtonLayoutRows();
            UpdateButtonLayoutStatus();
        }

        private void UpdateButtonLayoutStatus()
        {
            ButtonLayout staged;
            if (!ButtonLayout.TryCreate(_buttonLayoutOrder, _buttonLayoutNames, out staged))
            {
                _buttonLayoutStatus.Text = "名称无效，请检查后保存。";
                return;
            }
            _buttonLayoutStatus.Text = staged.Serialize() == _buttonLayout.Serialize() ? "" : "有未保存的更改";
        }

        private void SaveButtonLayout()
        {
            ButtonLayout staged;
            if (!ButtonLayout.TryCreate(_buttonLayoutOrder, _buttonLayoutNames, out staged))
            {
                _buttonLayoutStatus.Text = "名称无效，请检查后保存。";
                return;
            }
            try
            {
                ConfigurationProfiles.WriteText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ButtonLayout.FileName), staged.Serialize());
                _buttonLayout = staged;
                StageButtonLayout(staged);
                RenderButtonLayoutRows();
                ApplyButtonPresentation();
                _buttonLayoutStatus.Text = "已保存";
            }
            catch (IOException error) { _buttonLayoutStatus.Text = "保存失败 · " + error.Message; }
            catch (UnauthorizedAccessException error) { _buttonLayoutStatus.Text = "保存失败 · " + error.Message; }
        }
    }
}
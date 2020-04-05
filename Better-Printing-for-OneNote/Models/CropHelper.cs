﻿using Better_Printing_for_OneNote.AdditionalClasses;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfCropableImageControl;

namespace Better_Printing_for_OneNote.Models
{
    class CropHelper : NotifyBase
    {
        private FixedDocument _document;
        public FixedDocument Document
        {
            get
            {
                return _document;
            }
            set
            {
                _document = value;
                OnPropertyChanged(nameof(Document));
            }
        }

        private ObservableCollection<PageModel> Pages = new ObservableCollection<PageModel>();
        private CropsAndSkips CurrentCropsAndSkips;
        public readonly List<SignatureAdded> CurrentSignatures = new List<SignatureAdded>();
        private readonly List<DocumentChange> UndoChangeList = new List<DocumentChange>();
        private readonly List<DocumentChange> RedoChangeList = new List<DocumentChange>();

        private int MaxCropHeight;
        private double PageHeight;
        private double PageWidth;
        private double ContentHeight;
        private double ContentWidth;
        private Thickness Padding;
        private BitmapSource[] Images;

        public CropHelper(BitmapSource[] images)
        {
            Images = images;
        }

        public void InitializePages()
        {
            RedoChangeList.Clear();
            UndoChangeList.Clear();
            CurrentSignatures.Clear();


            var bigImageHeight = 0;
            foreach (var b in Images)
                bigImageHeight += b.PixelHeight;


            CurrentCropsAndSkips = new CropsAndSkips(new List<int>(), new List<Skip>());
            var positionY = 0;
            var first = true;
            while (positionY + MaxCropHeight < bigImageHeight)
            {
                var page = CreateNewPage(null);
                page.CropShift = positionY;
                positionY += page.CropHeight;
                CurrentCropsAndSkips.Crops.Add(page.CropHeight);
                Pages.Add(page);
                if (first)
                {
                    MaxCropHeight = page.MaxCropHeight;
                    first = false;
                }
            }

            // add last crop
            var lastPage = CreateNewPage(null);
            lastPage.CropShift = positionY;
            lastPage.CropHeight = bigImageHeight - positionY;
            CurrentCropsAndSkips.Crops.Add(bigImageHeight - positionY);
            Pages.Add(lastPage);


            var document = new FixedDocument();
            foreach (var p in Pages)
                document.Pages.Add(p.Page);
            Document = document;
        }

        public void UndoChange()
        {
            if (UndoChangeList.Count > 0)
            {
                if (UndoChangeList[UndoChangeList.Count - 1] is CropsAndSkips cropsAndSkips)
                {
                    CurrentCropsAndSkips = cropsAndSkips;
                    UpdatePages();
                }
                else if (UndoChangeList[UndoChangeList.Count - 1] is SignatureAdded signatureAdded)
                {
                    CurrentSignatures.Remove(signatureAdded);

                    foreach (var uiElement in signatureAdded.UIElements)
                        Pages[uiElement.Item1].RemoveUIElement(uiElement.Item2);
                }

                RedoChangeList.Add(UndoChangeList[UndoChangeList.Count - 1]);
                UndoChangeList.RemoveAt(UndoChangeList.Count - 1);
            }
        }

        public void RedoChange()
        {
            if (RedoChangeList.Count > 0)
            {
                if (RedoChangeList[RedoChangeList.Count - 1] is CropsAndSkips cropsAndSkips)
                {
                    UndoChangeList.Add(CurrentCropsAndSkips);
                    CurrentCropsAndSkips = cropsAndSkips;
                    UpdatePages();
                }
                else if (RedoChangeList[RedoChangeList.Count - 1] is SignatureAdded signatureAdded)
                {
                    CurrentSignatures.Add(signatureAdded);

                    foreach (var uiElement in signatureAdded.UIElements)
                        Pages[uiElement.Item1].AddUIElement(uiElement.Item2);
                }

                UndoChangeList.Add(RedoChangeList[RedoChangeList.Count - 1]);
                RedoChangeList.RemoveAt(RedoChangeList.Count - 1);
            }
        }

        public void SkipPage(int pageToSkip)
        {
            if (Pages.Count > 1)
            {
                var newCropsAndSkips = CurrentCropsAndSkips.Copy();

                newCropsAndSkips.Crops.RemoveAt(pageToSkip);
                var skipStart = CalculatePixelPosY_WholeImage(0, pageToSkip);
                var skipEnd = CalculatePixelPosY_WholeImage(CurrentCropsAndSkips.Crops[pageToSkip], pageToSkip);
                newCropsAndSkips.InsertSkip(new Skip() { SkipType = SkipType.Y, SkipStart = skipStart, SkipEnd = skipEnd });

                UndoChangeList.Add(CurrentCropsAndSkips);
                CurrentCropsAndSkips = newCropsAndSkips;
                UpdatePages();
            }
        }

        public void MergePages(int from, int to)
        {
            var newCropsAndSkips = CurrentCropsAndSkips.Copy();

            var newCropHeight = CurrentCropsAndSkips.Crops[from];
            for (int i = to; i > from; i--)
            {
                newCropHeight += CurrentCropsAndSkips.Crops[i];
                newCropsAndSkips.Crops.RemoveAt(i);
            }
            newCropsAndSkips.Crops[from] = newCropHeight;

            UndoChangeList.Add(CurrentCropsAndSkips);
            CurrentCropsAndSkips = newCropsAndSkips;
            UpdatePages();
        }

        /// <param name="percentage">the vertical position percentage relative to the page (with margin)</param>
        public void SplitPageAt(int pageToEdit, double percentage)
        {
            var splitAt = Pages[pageToEdit].CalculatePixelPosY(percentage);

            if (splitAt != Pages[pageToEdit].CropHeight)
            {
                var newCropsAndSkips = CurrentCropsAndSkips.Copy();

                if (newCropsAndSkips.Crops.Count - 1 > pageToEdit)
                    newCropsAndSkips.Crops[pageToEdit + 1] = CurrentCropsAndSkips.Crops[pageToEdit + 1] + (CurrentCropsAndSkips.Crops[pageToEdit] - splitAt);
                else
                    newCropsAndSkips.Crops.Add(CurrentCropsAndSkips.Crops[pageToEdit] - splitAt);
                newCropsAndSkips.Crops[pageToEdit] = splitAt;

                UndoChangeList.Add(CurrentCropsAndSkips);
                CurrentCropsAndSkips = newCropsAndSkips;
                UpdatePages();
            }
        }

        public void DeleteArea(int pageToEdit, double _start, double _end)
        {
            var start = CalculatePixelPosY_WholeImage(_start, pageToEdit);
            var end = CalculatePixelPosY_WholeImage(_end, pageToEdit);

            var newCropsAndSkips = CurrentCropsAndSkips.Copy();
            newCropsAndSkips.Crops[pageToEdit] = newCropsAndSkips.Crops[pageToEdit] - (Pages[pageToEdit].CalculatePixelPosY(_end) - Pages[pageToEdit].CalculatePixelPosY(_start));
            newCropsAndSkips.InsertSkip(new Skip() { SkipType = SkipType.Y, SkipStart = start, SkipEnd = end });

            UndoChangeList.Add(CurrentCropsAndSkips);
            CurrentCropsAndSkips = newCropsAndSkips;
            UpdatePages();
        }

        /// <summary>
        /// calculates the vertical pixel position relative to the whole image (with skips)
        /// </summary>
        /// <param name="percentage">the vertical position percentage relative to the page (with margin)</param>
        public int CalculatePixelPosY_WholeImage(double percentage, int page)
        {
            // add the page heights before the page
            var posY = 0;
            for (int i = 0; i < page; i++)
                posY += Pages[i].CropHeight;

            posY += Pages[page].CalculatePixelPosY(percentage);

            // add the skipped area before the page to get the actual height
            if (CurrentCropsAndSkips.Skips != null)
            {
                var relevantSkipHeight = 0;
                foreach (Skip s in CurrentCropsAndSkips.Skips)
                    if (s.SkipStart <= posY + relevantSkipHeight)
                        relevantSkipHeight += s.SkipHeight;
                posY += relevantSkipHeight;
            }
            return posY;
        }

        public double GetOptimalHeight(int pageIndex)
        {
            return Pages[pageIndex].CalculateOptimalCropHeight();
        }

        private void UpdatePages()
        {
            // remove all UIElements from all pages
            foreach (var pageModel in Pages)
            {
                FixedPage fp = pageModel.Page.Child;
                fp.Children.Clear();
            }

            // remove all pages
            Pages.Clear();

            // process crops n skips
            var skipsArrayList = new ArrayList(CurrentCropsAndSkips.Skips);
            var shiftY = 0;
            foreach (var cropHeight in CurrentCropsAndSkips.Crops)
            {
                var page = CreateNewPage(skipsArrayList);
                page.CropShift = shiftY;
                page.CropHeight = cropHeight;
                shiftY += cropHeight;
                Pages.Add(page);
            }

            // create document
            var document = new FixedDocument();
            foreach (var page in Pages)
                document.Pages.Add(page.Page);
            Document = document;


            // add signatures
            for (int i = 0; i < CurrentSignatures.Count; i++)
            {
                SignatureAdded signature = CurrentSignatures[i];
                // remove empty signatures
                if (string.IsNullOrWhiteSpace(signature.Text.Text))
                {
                    CurrentSignatures.RemoveAt(i);
                    UndoChangeList.Remove(signature);
                    i--;
                    continue;
                }

                signature.UIElements.Clear();

                // add signature ui element to every page
                for (int a = 0; a < Pages.Count; a++)
                {
                    TextBox tb = CreateSignatureTextBox(signature.Text, new Thickness(signature.X, signature.Y, 0, 0));
                    Pages[a].AddUIElement(tb);
                    signature.UIElements.Add((a, tb));
                }
            }
        }

    /// <summary>
    /// Add an editable TextBox to all pages of the document (returns specified textbox, for focusing)
    /// </summary>
    public TextBox InitialAddSignatureTb(double x, double y, int textboxToReturnPageIndex)
    {
        // match x and y coordinates to height/width of the content of the page
        x = x - Padding.Left;
        y = y - Padding.Top;

        if (x < 0 || x > ContentWidth)
            return null;
        if (y < 0 || y > ContentHeight)
            return null;

        // approximately center textbox on coordinates
        x -= 2;
        y -= 8;

        BindableText bindableText = new BindableText();
        SignatureAdded signatureAdded = new SignatureAdded(bindableText, x, y);

        return AddSignatureToPages(signatureAdded, textboxToReturnPageIndex);
    }

    private TextBox AddSignatureToPages(SignatureAdded signatureAdded, int textboxToReturnPageIndex = -1)
    {
        TextBox textbox = null;
        for (int i = 0; i < Pages.Count; i++)
        {
            TextBox tb = CreateSignatureTextBox(signatureAdded.Text, new Thickness(signatureAdded.X, signatureAdded.Y, 0, 0));
            Pages[i].AddUIElement(tb);
            signatureAdded.UIElements.Add((i, tb));
            if (i == textboxToReturnPageIndex)
                textbox = tb;
        }

        UndoChangeList.Add(signatureAdded);
        return textbox;
    }

    public void AddSignatures(List<SignatureAdded> signatures)
    {
        foreach (var signature in signatures)
        {
            if (!UndoChangeList.Exists(s => s is SignatureAdded && s.Equals(signature)))
            {
                var copy = signature.Copy();
                UndoChangeList.Add(copy);
                AddSignatureToPages(copy);
            }
        }
    }

    private TextBox CreateSignatureTextBox(BindableText text, Thickness margin)
    {
        TextBox tb = new TextBox { AcceptsReturn = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = margin };
        Binding binding = new Binding(nameof(text.Text));
        binding.Mode = BindingMode.TwoWay;
        binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
        binding.Source = text;
        tb.SetBinding(TextBox.TextProperty, binding);

        return tb;
    }

    public void UpdateFormat(double pageHeight, double pageWidth, double contentHeight, double contentWidth, Thickness padding)
    {
        if (PageHeight != pageHeight || PageWidth != pageWidth || contentHeight != ContentHeight || contentWidth != ContentWidth || Padding != padding)
        {
            PageHeight = pageHeight;
            PageWidth = pageWidth;
            ContentHeight = contentHeight;
            ContentWidth = contentWidth;
            Padding = padding;
            if (Pages.Count > 0)
                UpdatePages();
            else
                InitializePages();
        }
    }

    private PageModel CreateNewPage(ArrayList skips)
    {
        return new PageModel(Images, skips, PageHeight, PageWidth, ContentHeight, ContentWidth, Padding);
    }
}

public class DocumentChange { }

class CropsAndSkips : DocumentChange
{
    public List<int> Crops { get; set; }
    private List<Skip> _skips;
    public ReadOnlyCollection<Skip> Skips
    {
        get
        {
            return new ReadOnlyCollection<Skip>(_skips);
        }
    }

    public CropsAndSkips(List<int> crops, List<Skip> skips)
    {
        Crops = crops;
        _skips = skips;
    }

    public void InsertSkip(Skip skip)
    {
        for (int i = 0; i < Skips.Count; i++)
        {
            if (skip.SkipEnd < Skips[i].SkipStart)
            {
                // right position for the skip
                _skips.Insert(i, skip);
                return;
            }
            else
            {
                if ((skip.SkipStart <= Skips[i].SkipStart && skip.SkipEnd >= Skips[i].SkipEnd)
                    || (skip.SkipStart >= Skips[i].SkipStart && skip.SkipEnd >= Skips[i].SkipEnd && skip.SkipStart <= Skips[i].SkipEnd)
                    || (skip.SkipEnd <= Skips[i].SkipEnd))
                {
                    // skips are overlapping
                    skip.SkipStart = Math.Min(skip.SkipStart, Skips[i].SkipStart);
                    skip.SkipEnd = Math.Max(skip.SkipEnd, Skips[i].SkipEnd);
                    _skips.RemoveAt(i);
                    i--;
                }
            }
        }

        _skips.Add(skip);
    }

    public CropsAndSkips Copy()
    {
        var crops = new List<int>();
        foreach (var c in Crops)
            crops.Add(c);

        var skips = new List<Skip>();
        foreach (var s in Skips)
            skips.Add(s);

        return new CropsAndSkips(crops, skips);
    }
}

public class SignatureAdded : DocumentChange
{
    public List<(int, UIElement)> UIElements { get; set; } = new List<(int, UIElement)>();
    public BindableText Text { get; set; }
    public double X { get; set; }
    public double Y { get; set; }

    public SignatureAdded(BindableText text, double x, double y)
    {
        Text = text;
        X = x;
        Y = y;
    }

    public SignatureAdded() { }

    internal SignatureAdded Copy()
    {
        return new SignatureAdded() { Text = this.Text.Copy(), X = this.X, Y = this.Y };
    }

    internal bool Equals(SignatureAdded sa)
    {
        return this.X == sa.X && this.Y == sa.Y && this.Text.Text == sa.Text.Text;
    }
}

public class BindableText : NotifyBase
{
    private string text = string.Empty;
    public string Text
    {
        get => text;
        set
        {
            if (value != text)
            {
                text = value;
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    internal BindableText Copy()
    {
        return new BindableText() { Text = this.Text };
    }
}
}
﻿using System;
using System.Collections.Generic;
using System.Linq;
using SmartFormat.Core;
using SmartFormat.Core.Extensions;
using SmartFormat.Core.Output;
using SmartFormat.Core.Parsing;
using FormatException = SmartFormat.Core.FormatException;

namespace SmartFormat
{
    /// <summary>
    /// This class contains the Format method that constructs 
    /// the composite string by invoking each extension.
    /// </summary>
    public class SmartFormatter
    {
        #region: Constructor :

        public SmartFormatter()
        {
            this.Parser = new Parser();
        }

        public SmartFormatter(params object[] extensions)
        {
            this.Parser = new Parser();
            this.AddExtensions(extensions);
        }

        public SmartFormatter(ErrorAction errorAction, params object[] extensions)
        {
            this.Parser = new Parser(errorAction);
            this.AddExtensions(extensions);
            this.ErrorAction = errorAction;
        }

        #endregion

        #region: Extension Registration :

        private readonly List<ISource> sourceExtensions = new List<ISource>();
        private readonly List<IFormatter> formatterExtensions = new List<IFormatter>();
        /// <summary>
        /// Adds each extensions to this formatter.
        /// Each extension must implement ISource, IFormatter, or both.
        /// 
        /// An exception will be thrown if the extension doesn't implement those interfaces.
        /// </summary>
        /// <param name="extensions"></param>
        public void AddExtensions(params object[] extensions)
        {
            foreach (var extension in extensions)
            {
                // We need to filter each extension to the correct list:
                var source = extension as ISource;
                var formatter = extension as IFormatter;

                // If this object ISN'T a extension, throw an exception:
                if (source == null && formatter == null)
                    throw new ArgumentException(string.Format("{0} does not implement ISource nor IFormatter.", extension.GetType().FullName), "extensions");

                if (source != null)
                    sourceExtensions.Add(source);
                if (formatter != null)
                    formatterExtensions.Add(formatter);
            }

            // Search each extension for the "ExtensionPriority" 
            // attribute, and sort the lists accordingly.

            sourceExtensions.Sort(ExtensionPriorityAttribute.SourceComparer());
            formatterExtensions.Sort(ExtensionPriorityAttribute.FormatterComparer());
        }

        #endregion

        #region: Properties :

        public Parser Parser { get; set; }
        public IFormatProvider Provider { get; set; }
        public ErrorAction ErrorAction { get; set; }
        
        #endregion

        #region: Format Overloads :

        public string Format(string format, params object[] args)
        {
            var output = new StringOutput(format.Length + args.Length * 8);
            
            var formatParsed = Parser.ParseFormat(format);
            object current = (args != null && args.Length > 0) ? args[0] : args; // The first item is the default.
            var formatDetails = new FormatDetails(this, args, null);
            Format(output, formatParsed, current, formatDetails);

            return output.ToString();
        }

        public void FormatInto(IOutput output, string format, params object[] args)
        {
            var formatParsed = Parser.ParseFormat(format);
            object current = (args != null && args.Length > 0) ? args[0] : args; // The first item is the default.
            var formatDetails = new FormatDetails(this, args, null);
            Format(output, formatParsed, current, formatDetails);
        }

        public string FormatWithCache(ref FormatCache cache, string format, params object[] args)
        {
            var output = new StringOutput(format.Length + args.Length * 8);

            if (cache == null) cache = new FormatCache(this.Parser.ParseFormat(format));
            object current = (args != null && args.Length > 0) ? args[0] : args; // The first item is the default.
            var formatDetails = new FormatDetails(this, args, cache);
            Format(output, cache.Format, current, formatDetails);

            return output.ToString();
        }

        public void FormatWithCacheInto(ref FormatCache cache, IOutput output, string format, params object[] args)
        {
            if (cache == null) cache = new FormatCache(this.Parser.ParseFormat(format));
            object current = (args != null && args.Length > 0) ? args[0] : args; // The first item is the default.
            var formatDetails = new FormatDetails(this, args, cache);
            Format(output, cache.Format, current, formatDetails);
        }

        #endregion

        #region: Format :

        public void Format(IOutput output, Format format, object current, FormatDetails formatDetails)
        {
            Placeholder originalPlaceholder = formatDetails.Placeholder;
            foreach (var item in format.Items)
            {
                var literalItem = item as LiteralText;
                if (literalItem != null)
                {
                    formatDetails.Placeholder = originalPlaceholder;
                    output.Write(literalItem, formatDetails);
                    continue;
                } // Otherwise, the item is a placeholder.

                var placeholder = (Placeholder)item;
                object context = current;
                formatDetails.Placeholder = placeholder;

                bool handled;
                // Evaluate the selectors:
                foreach (var selector in placeholder.Selectors)
                {
                    handled = false;
                    var result = context;
                    InvokeSourceExtensions(context, selector, ref handled, ref result, formatDetails);
                    if (!handled)
                    {
                        // The selector wasn't handled.  It's probably not a property.
                        FormatError(selector, "Could not evaluate the selector: " + selector.Text, selector.startIndex, output, formatDetails);
                        context = null;
                        break;
                    }
                    context = result;
                }

                handled = false;
                try
                {
                    InvokeFormatterExtensions(context, placeholder.Format, ref handled, output, formatDetails);
                }
                catch (Exception ex)
                {
                    // An error occurred while formatting.
                    var errorIndex = placeholder.Format != null ? placeholder.Format.startIndex : placeholder.Selectors.Last().endIndex;
                    FormatError(item, ex, errorIndex, output, formatDetails);
                    continue;
                }

            }

        }

        private void InvokeSourceExtensions(object current, Selector selector, ref bool handled, ref object result, FormatDetails formatDetails)
        {
            foreach (var sourceExtension in this.sourceExtensions)
            {
                sourceExtension.EvaluateSelector(current, selector, ref handled, ref result, formatDetails);
                if (handled) break;
            }
        }
        private void InvokeFormatterExtensions(object current, Format format, ref bool handled, IOutput output, FormatDetails formatDetails)
        {
            foreach (var formatterExtension in this.formatterExtensions)
            {
                formatterExtension.EvaluateFormat(current, format, ref handled, output, formatDetails);
                if (handled) break;
            }
        }

        private void FormatError(FormatItem errorItem, string issue, int startIndex, IOutput output, FormatDetails formatDetails)
        {
            switch (this.ErrorAction)
            {
                case ErrorAction.Ignore:
                    return;
                case ErrorAction.ThrowError:
                    throw new FormatException(errorItem, issue, startIndex);
                case ErrorAction.OutputErrorInResult:
                    formatDetails.FormatError = new FormatException(errorItem, issue, startIndex);
                    output.Write(issue, formatDetails);
                    formatDetails.FormatError = null;
                    break;
            }
        }
        private void FormatError(FormatItem errorItem, Exception innerException, int startIndex, IOutput output, FormatDetails formatDetails)
        {
            switch (this.ErrorAction)
            {
                case ErrorAction.Ignore:
                    return;
                case ErrorAction.ThrowError:
                    throw new FormatException(errorItem, innerException, startIndex);
                case ErrorAction.OutputErrorInResult:
                    formatDetails.FormatError = new FormatException(errorItem, innerException, startIndex);
                    output.Write(innerException.Message, formatDetails);
                    formatDetails.FormatError = null;
                    break;
            }
        }

        #endregion

    }
}
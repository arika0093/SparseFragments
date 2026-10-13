# UI toolkit neutral binding certification

This record certifies that the generated observable proxy, the framework-neutral edit session, and the validation error store work with standard .NET binding patterns. It covers issue #262. The ViewModel shapes and the editable scoped row it certifies are the UiOrder and UiWidget samples in the UI frameworks guide, exercised here through the main test suite and through small WPF and WinForms smoke consumers.

## Scope of certification

Certification exercises scalar and nested property notifications, collection notifications, typed path queries, unchanged versus touched state, nested replacement, keyed reorder with stable identity, one-shot edit scopes, validation error display, and behavior after disposal. The framework-neutral checks live in the ComponentModel test group of the main suite. The WPF and WinForms checks live in the desktop binding test project, which references the core package and the generator only.

## What was verified on Windows

WPF verification binds a TextBox to an observable scalar with two-way PropertyChanged updates in both directions, binds a nested customer name through the child proxy, and binds a ListBox to a keyed line collection through ItemsSource, including add and keyed move. WinForms verification binds a TextBox to the same scalar shapes through DataBindings with OnPropertyChanged, binds a ListBox through a BindingSource over the observable list, and edits through the nested child proxy. Both platforms verify the explicit scope lifecycle: draft edits stay invisible to the parent model and to parent dirty state until TryCommit, Cancel and disposal discard the draft, and conflicting commits leave the parent untouched. Both platforms verify validation display through the store property-name API and its ErrorsChanged notifications. UI-threaded checks run on dedicated STA threads and pass repeatedly.

WinForms simple bindings activate with the host form, so the smoke tests show an invisible host form while asserting. The ListBox path needs an explicit ResetBindings call after adds because the observable list exposes IList and INotifyCollectionChanged rather than IBindingList. Section Binding boundaries below records what this implies.

## Compatibility matrix

| Area | WPF | WinForms | Avalonia | .NET MAUI | WinUI 3 |
| --- | --- | --- | --- | --- | --- |
| Scalar INotifyPropertyChanged binding | Verified on Windows | Verified on Windows | Manual probe | Manual probe | Manual probe |
| Nested proxy propagation | Verified on Windows | Verified on Windows | Manual probe | Manual probe | Manual probe |
| Collection change display | Verified on Windows | Verified with explicit refresh | Manual probe | Manual probe | Manual probe |
| Validation error display | Verified on Windows | Verified on Windows | Manual probe | Manual probe | Manual probe |
| One-shot edit scopes | Verified on Windows | Verified on Windows | Manual probe | Manual probe | Manual probe |
| Semantic dirty state | Verified on Windows | Verified on Windows | Manual probe | Manual probe | Manual probe |

Verified on Windows means the desktop binding project asserts the behavior on STA threads in this environment. Manual probe means the combination is expected to work through the same neutral interfaces but has no runner in this environment, so it needs a documented manual pass or a CI-gated probe before any claim is made. No Avalonia, MAUI, or WinUI SDK was added for this issue.

## Binding boundaries

Controls bind the session Observable proxy for editing and read display state from the session Current view. For a child edit, controls bind the scope Observable draft or its Model. The scope object itself exposes no bindable properties and implements no notification interfaces.

Scope editing is explicit. OpenScope opens a one-shot draft, TryCommit merges it into the parent with conflict checks, and Cancel or disposal discards it. A scope that committed or was canceled takes no further edits; the next edit opens a new scope. Grids bind row models, not scope objects, so row-at-a-time commit still goes through these calls in application code.

Issue #259 proposed IEditableObject on the generated scope and was closed as not planned. The scope is a one-shot draft while that interface expects repeated edit cycles on one bound row, and grids bind the row rather than the scope, so the interface would not enable grid integration. The test suite asserts the absence on scopes, sessions, and observables. No wrapper is provided to bridge the mismatch.

Two further boundaries follow the same policy. The observable list exposes IList for item controls and stays without IBindingList, and the observable stays without INotifyDataErrorInfo because error state lives in the separate validation store. A WinForms IBindingList adapter or any similar bridge ships only with demonstrated consumer evidence, which this issue does not claim.

## UI thread responsibility

Notifications fire synchronously on the mutating thread. The session raises PropertyChanged and transition events where the edit runs, collection views raise their change events there as well, and the validation store raises ErrorsChanged on the mutating thread under a lock. Marshaling to a UI thread stays with the application on every platform. The Blazor scope component already marshals through the Blazor synchronization context, and desktop handlers are expected to do the equivalent when edits originate off the UI thread.

## Known gaps and follow-ups

| Gap | Status |
| --- | --- |
| WinForms currency tracking without IBindingList | Documented; explicit refresh asserted; adapter only with further evidence |
| Grid row identity and multi-cycle row editing | Documented under issue #259 as rejected; explicit scope calls remain the contract |
| Avalonia binding probe | Open; needs a manual pass or a CI-gated runner |
| .NET MAUI binding probe | Open; needs a manual pass or a CI-gated runner |
| WinUI 3 binding probe | Open; needs a manual pass or a CI-gated runner |

No new UI toolkit package was created for this issue, and none is required by the core package. The Blazor package stays an ordinary optional consumer of the same neutral session.

## NativeAOT and trimming

This issue adds test and documentation files only and changes no runtime behavior, so the trimming and AOT surface is unchanged. Publish-time verification stays with the NativeAOT legs that issues #242 through #244 own. Those legs are CI-only and are noted here rather than repeated.

## Related work

Semantic field and collection state comes from issue #250, opt-in observable field subscriptions from issue #260, and the validation error store from issue #261. Blazor form integration around the same session is covered by issues #253 through #255, whose suites keep passing unchanged.

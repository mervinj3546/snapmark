import AppKit
import Carbon
import ScreenCaptureKit

// MARK: - Shapes

enum Tool: Int { case rect = 0, line, arrow, oval }

struct Shape {
    var tool: Tool
    var a: NSPoint
    var b: NSPoint
    var color: NSColor
    var width: CGFloat

    /// Shift-drag: squares for rectangles, 45° steps for lines and arrows.
    func snapped(_ p: NSPoint) -> NSPoint {
        if tool == .rect || tool == .oval {
            let d = max(abs(p.x - a.x), abs(p.y - a.y))
            return NSPoint(x: a.x + (p.x >= a.x ? d : -d), y: a.y + (p.y >= a.y ? d : -d))
        }
        let dx = p.x - a.x, dy = p.y - a.y
        let ang = (atan2(dy, dx) / (.pi / 4)).rounded() * (.pi / 4)
        let len = hypot(dx, dy)
        return NSPoint(x: a.x + cos(ang) * len, y: a.y + sin(ang) * len)
    }

    func draw() {
        color.setStroke()
        color.setFill()
        switch tool {
        case .rect:
            let r = NSRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
            let p = NSBezierPath(rect: r)
            p.lineWidth = width
            p.lineJoinStyle = .round
            p.stroke()
        case .oval:
            let r = NSRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
            let p = NSBezierPath(ovalIn: r)
            p.lineWidth = width
            p.stroke()
        case .line:
            let p = NSBezierPath()
            p.move(to: a); p.line(to: b)
            p.lineWidth = width
            p.lineCapStyle = .round
            p.stroke()
        case .arrow:
            let dx = b.x - a.x, dy = b.y - a.y
            let len = hypot(dx, dy)
            guard len > 0.5 else { return }
            let ux = dx / len, uy = dy / len
            // Fixed-size head: depends only on line width, capped so short arrows stay sane.
            let headLen = min(max(14, width * 4), len * 0.6)
            let headHalf = headLen * 0.5
            let base = NSPoint(x: b.x - ux * headLen, y: b.y - uy * headLen)
            let shaft = NSBezierPath()
            shaft.move(to: a)
            shaft.line(to: NSPoint(x: base.x + ux * 1, y: base.y + uy * 1))
            shaft.lineWidth = width
            shaft.lineCapStyle = .round
            shaft.stroke()
            let head = NSBezierPath()
            head.move(to: b)
            head.line(to: NSPoint(x: base.x - uy * headHalf, y: base.y + ux * headHalf))
            head.line(to: NSPoint(x: base.x + uy * headHalf, y: base.y - ux * headHalf))
            head.close()
            head.lineJoinStyle = .round
            head.lineWidth = 1
            head.fill()
            head.stroke()
        }
    }
}

// MARK: - Canvas

final class CanvasView: NSView {
    let image: NSImage
    var shapes: [Shape] = []
    var current: Shape?
    var tool: Tool = .arrow
    var color: NSColor = NSColor(srgbRed: 1, green: 0.1, blue: 0.1, alpha: 1)
    var lineWidth: CGFloat = 2

    init(image: NSImage) {
        self.image = image
        super.init(frame: NSRect(origin: .zero, size: image.size))
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }

    override func draw(_ dirtyRect: NSRect) {
        image.draw(in: bounds, from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: nil)
        shapes.forEach { $0.draw() }
        current?.draw()
    }

    private func point(_ e: NSEvent) -> NSPoint {
        let p = convert(e.locationInWindow, from: nil)
        return NSPoint(x: min(max(p.x, 0), bounds.width), y: min(max(p.y, 0), bounds.height))
    }

    override func mouseDown(with e: NSEvent) {
        let p = point(e)
        current = Shape(tool: tool, a: p, b: p, color: color, width: lineWidth)
    }

    override func mouseDragged(with e: NSEvent) {
        guard var s = current else { return }
        let p = point(e)
        s.b = e.modifierFlags.contains(.shift) ? s.snapped(p) : p
        current = s
        needsDisplay = true
    }

    override func mouseUp(with e: NSEvent) {
        if let s = current, hypot(s.b.x - s.a.x, s.b.y - s.a.y) > 3 {
            shapes.append(s)
        }
        current = nil
        needsDisplay = true
    }

    func undo() {
        guard !shapes.isEmpty else { NSSound.beep(); return }
        shapes.removeLast()
        needsDisplay = true
    }

    /// Renders at the image's full pixel resolution (Retina-sharp), independent of the window's screen.
    func renderBitmap() -> NSBitmapImageRep? {
        let rep = image.representations.first
        let pw = rep?.pixelsWide ?? Int(image.size.width)
        let ph = rep?.pixelsHigh ?? Int(image.size.height)
        guard let bmp = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pw, pixelsHigh: ph,
                                         bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                         colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        // Set the point size BEFORE making the context, or drawing lands in one corner at 1x.
        bmp.size = image.size
        guard let ctx = NSGraphicsContext(bitmapImageRep: bmp) else { return nil }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ctx
        image.draw(in: NSRect(origin: .zero, size: image.size))
        let flip = NSAffineTransform()
        flip.translateX(by: 0, yBy: image.size.height)
        flip.scaleX(by: 1, yBy: -1)
        flip.concat()
        shapes.forEach { $0.draw() }
        ctx.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()
        return bmp
    }
}

// MARK: - Editor window

final class EditorWindow: NSWindow {
    let canvas: CanvasView
    let toolPicker = NSSegmentedControl()
    var onClose: (() -> Void)?

    init(image: NSImage) {
        canvas = CanvasView(image: image)

        let toolbarHeight: CGFloat = 44
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let w = min(max(image.size.width, 560), screen.width * 0.9)
        let h = min(image.size.height + toolbarHeight, screen.height * 0.9)

        super.init(contentRect: NSRect(x: 0, y: 0, width: w, height: h),
                   styleMask: [.titled, .closable, .resizable, .miniaturizable],
                   backing: .buffered, defer: false)
        title = "Snapmark"
        isReleasedWhenClosed = false

        // Toolbar
        toolPicker.segmentCount = 3
        let items: [(String, String, String)] = [("rectangle", "Rectangle (R)", "R"),
                                                 ("line.diagonal", "Line (L)", "L"),
                                                 ("arrow.up.right", "Arrow (A)", "A")]
        for (i, it) in items.enumerated() {
            toolPicker.setImage(NSImage(systemSymbolName: it.0, accessibilityDescription: it.1), forSegment: i)
            toolPicker.setToolTip(it.1, forSegment: i)
            toolPicker.setWidth(40, forSegment: i)
        }
        toolPicker.trackingMode = .selectOne
        toolPicker.selectedSegment = Tool.arrow.rawValue
        toolPicker.target = self
        toolPicker.action = #selector(toolChanged)

        let colorWell = NSColorWell(style: .minimal)
        colorWell.color = canvas.color
        colorWell.target = self
        colorWell.action = #selector(colorChanged(_:))
        colorWell.toolTip = "Color"

        let widthPicker = NSSegmentedControl(labels: ["S", "M", "L"], trackingMode: .selectOne,
                                             target: self, action: #selector(widthChanged(_:)))
        widthPicker.selectedSegment = 1
        widthPicker.toolTip = "Line thickness"

        let undoButton = NSButton(image: NSImage(systemSymbolName: "arrow.uturn.backward", accessibilityDescription: "Undo")!,
                                  target: self, action: #selector(undo))
        undoButton.keyEquivalent = "z"
        undoButton.keyEquivalentModifierMask = .command
        undoButton.toolTip = "Undo (⌘Z)"

        let copyButton = NSButton(title: "Copy", target: self, action: #selector(copyImage))
        copyButton.image = NSImage(systemSymbolName: "doc.on.doc", accessibilityDescription: nil)
        copyButton.imagePosition = .imageLeading
        copyButton.bezelColor = .controlAccentColor
        copyButton.keyEquivalent = "c"
        copyButton.keyEquivalentModifierMask = .command
        copyButton.toolTip = "Copy to clipboard and close (⌘C or Return)"

        let spacer = NSView()
        spacer.setContentHuggingPriority(.init(1), for: .horizontal)

        let bar = NSStackView(views: [toolPicker, colorWell, widthPicker, undoButton, spacer, copyButton])
        bar.orientation = .horizontal
        bar.spacing = 10
        bar.edgeInsets = NSEdgeInsets(top: 8, left: 12, bottom: 8, right: 12)

        // Canvas in a scroll view (for captures larger than the window)
        let scroll = NSScrollView()
        scroll.documentView = canvas
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.autohidesScrollers = true
        scroll.backgroundColor = .windowBackgroundColor
        scroll.drawsBackground = true

        let root = NSStackView(views: [bar, scroll])
        root.orientation = .vertical
        root.spacing = 0
        root.alignment = .leading
        bar.translatesAutoresizingMaskIntoConstraints = false
        scroll.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            bar.widthAnchor.constraint(equalTo: root.widthAnchor),
            bar.heightAnchor.constraint(equalToConstant: toolbarHeight),
            scroll.widthAnchor.constraint(equalTo: root.widthAnchor),
        ])
        contentView = root
        center()
    }

    @objc func toolChanged() { canvas.tool = Tool(rawValue: toolPicker.selectedSegment) ?? .arrow }
    @objc func colorChanged(_ w: NSColorWell) { canvas.color = w.color }
    @objc func widthChanged(_ s: NSSegmentedControl) { canvas.lineWidth = [2.5, 4, 7][s.selectedSegment] }
    @objc func undo() { canvas.undo() }

    @objc func copyImage() {
        guard let bmp = canvas.renderBitmap(), let png = bmp.representation(using: .png, properties: [:]) else {
            NSSound.beep(); return
        }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.declareTypes([.png, .tiff], owner: nil)
        pb.setData(png, forType: .png)
        if let tiff = bmp.tiffRepresentation { pb.setData(tiff, forType: .tiff) }
        close()
    }

    override func keyDown(with e: NSEvent) {
        switch e.charactersIgnoringModifiers?.lowercased() {
        case "r": toolPicker.selectedSegment = 0; toolChanged()
        case "l": toolPicker.selectedSegment = 1; toolChanged()
        case "a": toolPicker.selectedSegment = 2; toolChanged()
        case "\r": copyImage()
        default: super.keyDown(with: e)
        }
    }

    override func cancelOperation(_ sender: Any?) { close() }

    override func close() {
        super.close()
        onClose?()
    }
}

// MARK: - Full-screen capture overlay (dim every display, select, annotate in place, copy)

/// Shared state for one capture across all displays.
final class Session {
    var tool: Tool = .arrow
    var color = NSColor(srgbRed: 1, green: 0.1, blue: 0.1, alpha: 1)
    var lineWidth: CGFloat = 2
    var windows: [OverlayWindow] = []
    var onEnd: (() -> Void)?

    func start(images: [(NSScreen, CGImage)]) {
        for (screen, cg) in images { windows.append(OverlayWindow(screen: screen, image: cg, session: self)) }
        NSApp.activate(ignoringOtherApps: true)
        for w in windows { w.orderFrontRegardless(); w.makeFirstResponder(w.overlay) }
        let mouse = NSEvent.mouseLocation
        (windows.first { NSMouseInRect(mouse, $0.frame, false) } ?? windows.first)?.makeKeyAndOrderFront(nil)
    }

    func selectionBegan(in v: OverlayView) {
        for w in windows where w.overlay !== v { w.overlay.clearSelection() }
    }

    func refreshTools() { windows.forEach { $0.overlay.updateToolPanel() } }

    func end() {
        windows.forEach { $0.orderOut(nil) }
        windows.removeAll()
        onEnd?()
    }
}

final class OverlayWindow: NSWindow {
    let overlay: OverlayView

    init(screen: NSScreen, image: CGImage, session: Session) {
        overlay = OverlayView(frame: NSRect(origin: .zero, size: screen.frame.size), image: image, session: session)
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        level = .screenSaver
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        contentView = overlay
        setFrame(screen.frame, display: false)
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

/// Dark rounded floating bar that holds buttons.
final class PanelView: NSVisualEffectView {
    let stack = NSStackView()

    init(vertical: Bool) {
        super.init(frame: .zero)
        appearance = NSAppearance(named: .darkAqua)
        material = .hudWindow
        blendingMode = .withinWindow
        state = .active
        wantsLayer = true
        layer?.cornerRadius = 8
        layer?.masksToBounds = true
        stack.orientation = vertical ? .vertical : .horizontal
        stack.spacing = 2
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 4, bottom: 4, right: 4)
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
        isHidden = true
    }
    required init?(coder: NSCoder) { fatalError() }

    override func resetCursorRects() { addCursorRect(bounds, cursor: .arrow) }
}

/// Small color square; click opens a swatch menu.
final class ColorSquare: NSButton {
    var color: NSColor = .red { didSet { needsDisplay = true } }

    override func draw(_ dirtyRect: NSRect) {
        let r = NSRect(x: bounds.midX - 7, y: bounds.midY - 7, width: 14, height: 14)
        color.setFill()
        let p = NSBezierPath(roundedRect: r, xRadius: 2, yRadius: 2)
        p.fill()
        NSColor(white: 1, alpha: 0.85).setStroke()
        p.lineWidth = 1
        p.stroke()
    }
}

final class OverlayView: NSView {
    let cg: CGImage
    let image: NSImage
    unowned let session: Session
    var selection: NSRect?
    var shapes: [Shape] = []
    var current: Shape?

    enum Drag { case none, select(NSPoint), draw, resize(l: Bool, r: Bool, t: Bool, b: Bool, start: NSRect) }
    var drag: Drag = .none

    let toolPanel = PanelView(vertical: true)
    let actionBar = PanelView(vertical: false)
    var toolButtons: [NSButton] = []
    let colorSquare = ColorSquare()
    let widthField = NSTextField(string: "2")
    static let swatches: [(String, NSColor)] = [
        ("Red", NSColor(srgbRed: 1, green: 0.1, blue: 0.1, alpha: 1)),
        ("Orange", .systemOrange), ("Yellow", .systemYellow), ("Green", .systemGreen),
        ("Blue", .systemBlue), ("Purple", .systemPurple), ("Black", .black), ("White", .white),
    ]

    init(frame: NSRect, image cg: CGImage, session: Session) {
        self.cg = cg
        self.image = NSImage(cgImage: cg, size: frame.size)
        self.session = session
        super.init(frame: frame)
        buildPanels()
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }

    // MARK: Panels

    private func iconButton(_ symbol: String, _ tip: String, _ action: Selector) -> NSButton {
        let img = NSImage(systemSymbolName: symbol, accessibilityDescription: tip)!
            .withSymbolConfiguration(.init(pointSize: 15, weight: .medium))!
        let b = NSButton(image: img, target: self, action: action)
        b.isBordered = false
        b.toolTip = tip
        b.contentTintColor = .white
        b.wantsLayer = true
        b.layer?.cornerRadius = 5
        b.translatesAutoresizingMaskIntoConstraints = false
        b.widthAnchor.constraint(equalToConstant: 30).isActive = true
        b.heightAnchor.constraint(equalToConstant: 28).isActive = true
        return b
    }

    private func buildPanels() {
        let tools: [(Tool, String, String)] = [(.arrow, "arrow.up.right", "Arrow (A)"),
                                               (.line, "line.diagonal", "Line (L)"),
                                               (.rect, "rectangle", "Rectangle (R)"),
                                               (.oval, "circle", "Oval (O)")]
        for (t, sym, tip) in tools {
            let b = iconButton(sym, tip, #selector(toolClicked(_:)))
            b.tag = t.rawValue
            toolButtons.append(b)
            toolPanel.stack.addArrangedSubview(b)
        }
        colorSquare.isBordered = false
        colorSquare.title = ""
        colorSquare.target = self
        colorSquare.action = #selector(showColors(_:))
        colorSquare.toolTip = "Color"
        colorSquare.translatesAutoresizingMaskIntoConstraints = false
        colorSquare.widthAnchor.constraint(equalToConstant: 30).isActive = true
        colorSquare.heightAnchor.constraint(equalToConstant: 28).isActive = true
        toolPanel.stack.addArrangedSubview(colorSquare)
        toolPanel.stack.addArrangedSubview(iconButton("lineweight", "Line thickness in px (W)", #selector(focusWidth)))
        let fmt = NumberFormatter()
        fmt.minimum = 1
        fmt.maximum = 50
        fmt.maximumFractionDigits = 1
        widthField.formatter = fmt
        widthField.alignment = .center
        widthField.font = .monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        widthField.textColor = .white
        widthField.backgroundColor = NSColor(white: 1, alpha: 0.12)
        widthField.drawsBackground = true
        widthField.isBordered = false
        widthField.focusRingType = .none
        widthField.toolTip = "Line thickness in px (1–50). Type a value, then press Return."
        widthField.cell?.sendsActionOnEndEditing = true
        widthField.target = self
        widthField.action = #selector(widthChanged)
        widthField.translatesAutoresizingMaskIntoConstraints = false
        widthField.widthAnchor.constraint(equalToConstant: 30).isActive = true
        toolPanel.stack.addArrangedSubview(widthField)
        toolPanel.stack.addArrangedSubview(iconButton("arrow.uturn.backward", "Undo (⌘Z)", #selector(undo)))

        let copy = iconButton("doc.on.doc", "Copy and close (⌘C or Return)", #selector(copyImage))
        copy.contentTintColor = .systemBlue
        actionBar.stack.addArrangedSubview(copy)
        actionBar.stack.addArrangedSubview(iconButton("square.and.arrow.down", "Save to Desktop (⌘S)", #selector(saveImage)))
        actionBar.stack.addArrangedSubview(iconButton("xmark", "Close (Esc)", #selector(closeOverlay)))

        addSubview(toolPanel)
        addSubview(actionBar)
        updateToolPanel()
    }

    func updateToolPanel() {
        for b in toolButtons {
            b.layer?.backgroundColor = b.tag == session.tool.rawValue ? NSColor(white: 1, alpha: 0.22).cgColor : NSColor.clear.cgColor
        }
        if widthField.currentEditor() == nil { widthField.doubleValue = Double(session.lineWidth) }
        colorSquare.color = session.color
    }

    @objc func showColors(_ sender: NSButton) {
        let menu = NSMenu()
        for (i, (name, c)) in Self.swatches.enumerated() {
            let item = NSMenuItem(title: name, action: #selector(pickSwatch(_:)), keyEquivalent: "")
            item.target = self
            item.tag = i
            item.image = NSImage(size: NSSize(width: 14, height: 14), flipped: false) { r in
                c.setFill()
                let p = NSBezierPath(roundedRect: r.insetBy(dx: 0.5, dy: 0.5), xRadius: 2, yRadius: 2)
                p.fill()
                NSColor(white: 0.5, alpha: 0.6).setStroke(); p.stroke()
                return true
            }
            menu.addItem(item)
        }
        menu.popUp(positioning: nil, at: NSPoint(x: sender.bounds.maxX + 4, y: 0), in: sender)
    }

    @objc func pickSwatch(_ item: NSMenuItem) {
        session.color = Self.swatches[item.tag].1
        session.refreshTools()
    }

    private func hidePanels() { toolPanel.isHidden = true; actionBar.isHidden = true }

    private func layoutPanels() {
        guard let s = selection else { hidePanels(); return }
        let gap: CGFloat = 8, m: CGFloat = 4
        let tp = toolPanel.fittingSize, ab = actionBar.fittingSize

        // Tools: right of the selection, top-aligned. Left side or inside when no room.
        var x = s.maxX + gap
        if x + tp.width > bounds.maxX - m { x = s.minX - gap - tp.width }
        if x < m { x = s.maxX - gap - tp.width }
        let y = min(max(m, s.minY), bounds.maxY - tp.height - m)
        toolPanel.frame = NSRect(x: x, y: y, width: tp.width, height: tp.height)

        // Actions: below the selection, right-aligned. Above or inside when no room.
        var ay = s.maxY + gap
        if ay + ab.height > bounds.maxY - m { ay = s.minY - gap - ab.height }
        if ay < m { ay = s.maxY - gap - ab.height }
        let ax = min(max(m, s.maxX - ab.width), bounds.maxX - ab.width - m)
        actionBar.frame = NSRect(x: ax, y: ay, width: ab.width, height: ab.height)

        toolPanel.isHidden = false
        actionBar.isHidden = false
        window?.invalidateCursorRects(for: self)
    }

    func clearSelection() {
        selection = nil; shapes.removeAll(); current = nil
        hidePanels()
        needsDisplay = true
    }

    // MARK: Drawing

    override func draw(_ dirtyRect: NSRect) {
        image.draw(in: bounds, from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: nil)
        let dim = NSBezierPath(rect: bounds)
        if let s = selection { dim.append(NSBezierPath(rect: s)); dim.windingRule = .evenOdd }
        NSColor(white: 0, alpha: 0.45).setFill()
        dim.fill()
        guard let s = selection else { return }

        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: s).addClip()
        shapes.forEach { $0.draw() }
        current?.draw()
        NSGraphicsContext.restoreGraphicsState()

        // Dashed border
        let border = NSBezierPath(rect: s.insetBy(dx: -0.5, dy: -0.5))
        border.lineWidth = 1
        NSColor.white.setStroke(); border.stroke()
        border.setLineDash([4, 4], count: 2, phase: 0)
        NSColor.black.setStroke(); border.stroke()

        // Resize handles
        for (p, _) in handles(s) {
            let h = NSRect(x: p.x - 3.5, y: p.y - 3.5, width: 7, height: 7)
            NSColor.white.setFill(); h.fill()
            let hb = NSBezierPath(rect: h); hb.lineWidth = 1
            NSColor(white: 0, alpha: 0.6).setStroke(); hb.stroke()
        }

        // Pixel size label
        let scale = CGFloat(cg.width) / bounds.width
        let text = "\(Int((s.width * scale).rounded()))×\(Int((s.height * scale).rounded()))" as NSString
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.monospacedDigitSystemFont(ofSize: 11, weight: .medium),
                                                    .foregroundColor: NSColor.white]
        let ts = text.size(withAttributes: attrs)
        var lr = NSRect(x: s.minX, y: s.minY - ts.height - 10, width: ts.width + 10, height: ts.height + 4)
        if lr.minY < 2 { lr.origin.y = s.minY + 4; lr.origin.x = s.minX + 4 }
        NSColor(white: 0, alpha: 0.75).setFill()
        NSBezierPath(roundedRect: lr, xRadius: 4, yRadius: 4).fill()
        text.draw(at: NSPoint(x: lr.minX + 5, y: lr.minY + 2), withAttributes: attrs)
    }

    /// 8 handles with the edges each one moves (left, right, top, bottom).
    private func handles(_ s: NSRect) -> [(NSPoint, (Bool, Bool, Bool, Bool))] {
        let xs: [(CGFloat, Bool, Bool)] = [(s.minX, true, false), (s.midX, false, false), (s.maxX, false, true)]
        let ys: [(CGFloat, Bool, Bool)] = [(s.minY, true, false), (s.midY, false, false), (s.maxY, false, true)]
        var out: [(NSPoint, (Bool, Bool, Bool, Bool))] = []
        for x in xs { for y in ys where x.1 || x.2 || y.1 || y.2 {
            out.append((NSPoint(x: x.0, y: y.0), (x.1, x.2, y.1, y.2)))
        } }
        return out
    }

    // MARK: Mouse

    private func point(_ e: NSEvent) -> NSPoint {
        let p = convert(e.locationInWindow, from: nil)
        return NSPoint(x: min(max(p.x, 0), bounds.width), y: min(max(p.y, 0), bounds.height))
    }

    override func mouseDown(with e: NSEvent) {
        window?.makeFirstResponder(self)
        let p = point(e)
        if let s = selection, let h = handles(s).first(where: { abs($0.0.x - p.x) <= 7 && abs($0.0.y - p.y) <= 7 }) {
            drag = .resize(l: h.1.0, r: h.1.1, t: h.1.2, b: h.1.3, start: s)
            hidePanels()
            return
        }
        if let s = selection, s.contains(p) {
            current = Shape(tool: session.tool, a: p, b: p, color: session.color, width: session.lineWidth)
            drag = .draw
            return
        }
        session.selectionBegan(in: self)
        clearSelection()
        drag = .select(p)
    }

    override func mouseDragged(with e: NSEvent) {
        let p = point(e)
        switch drag {
        case .select(let a):
            selection = NSRect(x: min(a.x, p.x), y: min(a.y, p.y), width: abs(p.x - a.x), height: abs(p.y - a.y))
        case .draw:
            guard var sh = current, let s = selection else { return }
            let q = NSPoint(x: min(max(p.x, s.minX), s.maxX), y: min(max(p.y, s.minY), s.maxY))
            sh.b = e.modifierFlags.contains(.shift) ? sh.snapped(q) : q
            current = sh
        case let .resize(l, r, t, b, st):
            let minX = l ? p.x : st.minX, maxX = r ? p.x : st.maxX
            let minY = t ? p.y : st.minY, maxY = b ? p.y : st.maxY
            selection = NSRect(x: min(minX, maxX), y: min(minY, maxY), width: abs(maxX - minX), height: abs(maxY - minY))
        case .none: return
        }
        needsDisplay = true
    }

    override func mouseUp(with e: NSEvent) {
        switch drag {
        case .select, .resize:
            if let s = selection, s.width < 4 || s.height < 4 { selection = nil }
            layoutPanels()
        case .draw:
            if let s = current, hypot(s.b.x - s.a.x, s.b.y - s.a.y) > 3 { shapes.append(s) }
            current = nil
        case .none: break
        }
        drag = .none
        needsDisplay = true
    }

    // MARK: Keys and actions

    override func keyDown(with e: NSEvent) {
        if e.keyCode == 53 { closeOverlay(); return } // Esc
        switch e.charactersIgnoringModifiers?.lowercased() {
        case "r": setTool(.rect)
        case "o": setTool(.oval)
        case "l": setTool(.line)
        case "a": setTool(.arrow)
        case "w": focusWidth()
        case "\r": copyImage()
        default: super.keyDown(with: e)
        }
    }

    override func performKeyEquivalent(with e: NSEvent) -> Bool {
        guard e.modifierFlags.contains(.command), window?.isKeyWindow == true else { return super.performKeyEquivalent(with: e) }
        switch e.charactersIgnoringModifiers?.lowercased() {
        case "c": copyImage(); return true
        case "z": undo(); return true
        case "s": saveImage(); return true
        default: return super.performKeyEquivalent(with: e)
        }
    }

    private func setTool(_ t: Tool) { session.tool = t; session.refreshTools() }
    @objc func toolClicked(_ b: NSButton) { setTool(Tool(rawValue: b.tag) ?? .arrow) }
    @objc func focusWidth() { window?.makeFirstResponder(widthField) }
    @objc func widthChanged() {
        let v = widthField.doubleValue
        if v >= 1 { session.lineWidth = CGFloat(min(v, 50)) }
        session.refreshTools()
        if window?.firstResponder === widthField.currentEditor() { window?.makeFirstResponder(self) }
    }
    @objc func closeOverlay() { session.end() }

    @objc func undo() {
        guard !shapes.isEmpty else { NSSound.beep(); return }
        shapes.removeLast()
        needsDisplay = true
    }

    /// Crops the frozen screen to the selection at full pixel resolution and draws the shapes on top.
    func renderSelection() -> NSBitmapImageRep? {
        guard let s = selection else { return nil }
        let scale = CGFloat(cg.width) / bounds.width
        let px = NSRect(x: s.minX * scale, y: s.minY * scale, width: s.width * scale, height: s.height * scale).integral
        guard let crop = cg.cropping(to: px),
              let bmp = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: crop.width, pixelsHigh: crop.height,
                                         bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                         colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        // Set the point size BEFORE making the context, or drawing lands in one corner at 1x.
        bmp.size = s.size
        guard let ctx = NSGraphicsContext(bitmapImageRep: bmp) else { return nil }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ctx
        ctx.cgContext.draw(crop, in: CGRect(origin: .zero, size: s.size))
        let t = NSAffineTransform()
        t.translateX(by: 0, yBy: s.height)
        t.scaleX(by: 1, yBy: -1)
        t.translateX(by: -s.minX, yBy: -s.minY)
        t.concat()
        shapes.forEach { $0.draw() }
        ctx.flushGraphics()
        NSGraphicsContext.restoreGraphicsState()
        return bmp
    }

    @objc func copyImage() {
        guard let bmp = renderSelection(), let png = bmp.representation(using: .png, properties: [:]) else { NSSound.beep(); return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.declareTypes([.png, .tiff], owner: nil)
        pb.setData(png, forType: .png)
        if let tiff = bmp.tiffRepresentation { pb.setData(tiff, forType: .tiff) }
        session.end()
    }

    @objc func saveImage() {
        guard let png = renderSelection()?.representation(using: .png, properties: [:]) else { NSSound.beep(); return }
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
        let url = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Desktop/Snapmark \(f.string(from: Date())).png")
        do { try png.write(to: url); session.end() } catch { NSSound.beep() }
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate {
    static let shared = AppDelegate()
    var statusItem: NSStatusItem!
    var editors: [EditorWindow] = []
    var hotKeyRef: EventHotKeyRef?
    var capturing = false
    var session: Session?

    func applicationDidFinishLaunching(_ n: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "pencil.and.outline", accessibilityDescription: "Snapmark")

        // Left click captures right away. Right click (or Ctrl-click) shows Quit.
        statusItem.button?.target = self
        statusItem.button?.action = #selector(statusClicked)
        statusItem.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])
        statusItem.button?.toolTip = "Snapmark: click to capture (⌘⇧2). Right-click to quit."

        registerHotKey()
        buildMainMenu()

        // `Snapmark --open file.png` opens an existing image in the editor.
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--open"), i + 1 < args.count, let img = NSImage(contentsOfFile: args[i + 1]) {
            openEditor(img)
        } else {
            capture() // Launched from the Dock or Finder: start a capture right away.
        }
    }

    @objc func statusClicked() {
        let e = NSApp.currentEvent
        if e?.type == .rightMouseUp || e?.modifierFlags.contains(.control) == true {
            let menu = NSMenu()
            menu.addItem(NSMenuItem(title: "Quit Snapmark", action: #selector(NSApplication.terminate(_:)), keyEquivalent: ""))
            statusItem.menu = menu
            statusItem.button?.performClick(nil)
            statusItem.menu = nil
        } else {
            capture()
        }
    }

    /// Clicking the Dock icon while running starts a capture.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        capture()
        return false
    }

    func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        let cap = NSMenuItem(title: "Capture Area", action: #selector(capture), keyEquivalent: "2")
        cap.keyEquivalentModifierMask = [.command, .shift]
        cap.target = self
        appMenu.addItem(cap)
        appMenu.addItem(.separator())
        appMenu.addItem(NSMenuItem(title: "Quit Snapmark", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        appItem.submenu = appMenu
        NSApp.mainMenu = main
    }

    /// Global ⌘⇧2 via Carbon (needs no Accessibility permission).
    func registerHotKey() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            DispatchQueue.main.async { AppDelegate.shared.capture() }
            return noErr
        }, 1, &spec, nil, nil)
        let id = EventHotKeyID(signature: OSType(0x534E4D4B), id: 1) // 'SNMK'
        RegisterEventHotKey(UInt32(kVK_ANSI_2), UInt32(cmdKey | shiftKey), id, GetApplicationEventTarget(), 0, &hotKeyRef)
    }

    /// Freezes every display, dims it, and lets you select and annotate in place.
    @objc func capture() {
        guard !capturing else { return }
        capturing = true
        grabScreens { pairs in
            guard !pairs.isEmpty else { self.capturing = false; NSSound.beep(); return }
            let s = Session()
            s.onEnd = { [weak self] in
                self?.session = nil
                self?.capturing = false
                NSApp.hide(nil) // give focus back to the previous app so you can paste right away
            }
            self.session = s
            s.start(images: pairs)
        }
    }

    func grabScreens(_ done: @escaping ([(NSScreen, CGImage)]) -> Void) {
        SCShareableContent.getExcludingDesktopWindows(false, onScreenWindowsOnly: true) { content, _ in
            DispatchQueue.main.async {
                guard let content else { done([]); return }
                var results: [(NSScreen, CGImage)] = []
                let group = DispatchGroup()
                for screen in NSScreen.screens {
                    guard let id = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID,
                          let d = content.displays.first(where: { $0.displayID == id }) else { continue }
                    let cfg = SCStreamConfiguration()
                    cfg.width = Int(screen.frame.width * screen.backingScaleFactor)
                    cfg.height = Int(screen.frame.height * screen.backingScaleFactor)
                    cfg.showsCursor = false
                    cfg.captureResolution = .best
                    group.enter()
                    SCScreenshotManager.captureImage(contentFilter: SCContentFilter(display: d, excludingWindows: []),
                                                     configuration: cfg) { img, _ in
                        DispatchQueue.main.async {
                            if let img { results.append((screen, img)) }
                            group.leave()
                        }
                    }
                }
                group.notify(queue: .main) { done(results) }
            }
        }
    }

    func openEditor(_ img: NSImage) {
        let w = EditorWindow(image: img)
        w.onClose = { [weak self, weak w] in self?.editors.removeAll { $0 === w } }
        editors.append(w)
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
        w.makeFirstResponder(w.canvas)
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
app.delegate = AppDelegate.shared
app.run()

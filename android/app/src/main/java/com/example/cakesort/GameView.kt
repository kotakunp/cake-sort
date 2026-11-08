package com.example.cakesort

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Typeface
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.TypedValue
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.sin

/**
 * All game presentation and input lives in this view. The engine is only mutated after a
 * deliberate player action; event-driven Canvas animations never affect game rules.
 */
class GameView(context: Context) : View(context) {

    companion object {
        private const val PREFS_NAME = "cake_sort_progress"
        private const val KEY_LEVEL = "level"
        private const val KEY_HIGH_SCORE = "high_score"
        private const val KEY_SOUND = "sound"
        private const val BOOSTERS_PER_LEVEL = 3
        private const val MERGE_DURATION = 220L
        private const val SERVE_DURATION = 320L
        private const val MESSAGE_DURATION = 1500L

        private val CREAM = 0xFFFFF8EC.toInt()
        private val PANEL = 0xFFE7C9A9.toInt()
        private val PANEL_EDGE = 0xFFC59F7B.toInt()
        private val COCOA = 0xFF4E342E.toInt()
        private val MUTED = 0xFF8D6E63.toInt()
        private val PLATE = 0xFFF9F6F0.toInt()
        private val PLATE_EDGE = 0xFFC8B6A6.toInt()
        private val ACCENT = 0xFFC63D45.toInt()
        private val GOLD = 0xFFFFB300.toInt()
        private val DISABLED = 0xFFD8CEC5.toInt()

        private val FLAVORS = intArrayOf(
            0xFFE53935.toInt(), // red velvet
            0xFF6D4C41.toInt(), // chocolate
            0xFFFFF59D.toInt(), // vanilla
            0xFF66BB6A.toInt(), // matcha
            0xFF5C6BC0.toInt(), // blueberry
            0xFFF48FB1.toInt(), // strawberry
            0xFFFB8C00.toInt(), // orange
            0xFFAB47BC.toInt()  // grape
        )
    }

    private data class MergeAnimation(
        val from: Int,
        val to: Int,
        val flavor: Int,
        val count: Int,
        val start: Long,
        val end: Long
    )

    private data class ServeAnimation(
        val plate: Int,
        val flavor: Int,
        val points: Int,
        val combo: Int,
        val start: Long,
        val end: Long
    )

    private val preferences = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
    private val density = resources.displayMetrics.density
    private var engine = Engine(
        preferences.getInt(KEY_LEVEL, 0).coerceAtLeast(0),
        System.currentTimeMillis()
    )
    private var highScore = preferences.getInt(KEY_HIGH_SCORE, 0)
    private var soundEnabled = preferences.getBoolean(KEY_SOUND, true)

    private var hammers = BOOSTERS_PER_LEVEL
    private var shuffles = BOOSTERS_PER_LEVEL
    private var hammerArmed = false
    private var selectedSlot = -1

    private var draggingSlot = -1
    private var dragStarted = false
    private var downX = 0f
    private var downY = 0f
    private var touchX = 0f
    private var touchY = 0f
    private val touchSlop = ViewConfiguration.get(context).scaledTouchSlop

    private val mergeAnimations = mutableListOf<MergeAnimation>()
    private val serveAnimations = mutableListOf<ServeAnimation>()
    private var overlayAt = 0L

    private var message = ""
    private var messageUntil = 0L

    private val hudRect = RectF()
    private val boardRect = RectF()
    private val boosterRect = RectF()
    private val hammerRect = RectF()
    private val shuffleRect = RectF()
    private val slotRect = RectF()
    private val soundRect = RectF()
    private val overlayButtonRect = RectF()
    private val workRect = RectF()
    private val ovalRect = RectF()

    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeJoin = Paint.Join.ROUND
        strokeCap = Paint.Cap.ROUND
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = COCOA
        textAlign = Paint.Align.LEFT
    }
    private val wedgePath = Path()
    private val iconPath = Path()

    private val soundHandler = Handler(Looper.getMainLooper())
    private var toneGenerator: ToneGenerator? = null
    private var released = false

    init {
        isClickable = true
        isFocusable = true
        contentDescription = "Cake Sort Puzzle game board"
    }

    private fun dp(value: Float): Float = value * density

    private fun sp(value: Float): Float = TypedValue.applyDimension(
        TypedValue.COMPLEX_UNIT_SP,
        value,
        resources.displayMetrics
    )

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        val margin = dp(12f)
        hudRect.set(margin, margin, w - margin, margin + dp(86f))
        slotRect.set(margin, h - dp(136f), w - margin, h - dp(10f))
        boosterRect.set(margin, slotRect.top - dp(66f), w - margin, slotRect.top - dp(10f))

        val gap = dp(8f)
        val halfButton = (boosterRect.width() - gap) / 2f
        hammerRect.set(
            boosterRect.left,
            boosterRect.top,
            boosterRect.left + halfButton,
            boosterRect.bottom
        )
        shuffleRect.set(hammerRect.right + gap, boosterRect.top, boosterRect.right, boosterRect.bottom)

        val boardTop = hudRect.bottom + dp(10f)
        val availableHeight = boosterRect.top - dp(10f) - boardTop
        val boardSide = minOf(w - 2f * margin, availableHeight)
        boardRect.set((w - boardSide) / 2f, boardTop, (w + boardSide) / 2f, boardTop + boardSide)

        val soundSize = dp(40f)
        soundRect.set(hudRect.right - dp(10f) - soundSize, hudRect.top + dp(10f),
            hudRect.right - dp(10f), hudRect.top + dp(10f) + soundSize)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawColor(CREAM)
        val now = SystemClock.uptimeMillis()
        mergeAnimations.removeAll { now > it.end + 100L }
        serveAnimations.removeAll { now > it.end + 100L }

        drawHud(canvas)
        drawBoard(canvas, now)
        drawMergeAnimations(canvas, now)
        drawServeAnimations(canvas, now)
        drawBoosters(canvas)
        drawSlots(canvas)
        drawDragGhost(canvas)
        drawMessage(canvas, now)

        val showOverlay = overlayVisible(now)
        if (showOverlay) drawOverlay(canvas)

        if (mergeAnimations.isNotEmpty() || serveAnimations.isNotEmpty() ||
            (engine.state != GameState.PLAYING && !showOverlay)
        ) {
            postInvalidateOnAnimation()
        }
    }

    private fun drawText(
        canvas: Canvas,
        value: String,
        x: Float,
        baseline: Float,
        size: Float,
        color: Int = COCOA,
        align: Paint.Align = Paint.Align.LEFT,
        bold: Boolean = false
    ) {
        textPaint.color = color
        textPaint.textSize = sp(size)
        textPaint.textAlign = align
        textPaint.typeface = if (bold) Typeface.DEFAULT_BOLD else Typeface.DEFAULT
        canvas.drawText(value, x, baseline, textPaint)
    }

    private fun drawHud(canvas: Canvas) {
        fillPaint.color = 0xFFFFFCF5.toInt()
        canvas.drawRoundRect(hudRect, dp(18f), dp(18f), fillPaint)
        strokePaint.color = 0xFFE9D7C3.toInt()
        strokePaint.strokeWidth = dp(1f)
        canvas.drawRoundRect(hudRect, dp(18f), dp(18f), strokePaint)

        workRect.set(hudRect.left + dp(10f), hudRect.top + dp(12f),
            hudRect.left + dp(72f), hudRect.top + dp(48f))
        fillPaint.color = ACCENT
        canvas.drawRoundRect(workRect, dp(12f), dp(12f), fillPaint)
        drawText(canvas, "Lv ${engine.level + 1}", workRect.centerX(), workRect.centerY() + sp(6f),
            17f, Color.WHITE, Paint.Align.CENTER, true)

        val scoreX = hudRect.left + dp(82f)
        drawText(canvas, "Score ${engine.score} / ${engine.targetScore}", scoreX,
            hudRect.top + sp(28f), 18f, COCOA, Paint.Align.LEFT, true)
        drawText(canvas, "Cakes served: ${engine.cakesServed}", scoreX,
            hudRect.top + sp(49f), 13f, MUTED)
        drawText(canvas, "Best: $highScore", scoreX, hudRect.top + sp(67f), 11f, MUTED)

        workRect.set(scoreX, hudRect.bottom - dp(11f), soundRect.left - dp(10f), hudRect.bottom - dp(4f))
        fillPaint.color = 0xFFEADAC8.toInt()
        canvas.drawRoundRect(workRect, dp(4f), dp(4f), fillPaint)
        val progress = (engine.score.toFloat() / engine.targetScore).coerceIn(0f, 1f)
        if (progress > 0f) {
            workRect.right = workRect.left + workRect.width() * progress
            fillPaint.color = ACCENT
            canvas.drawRoundRect(workRect, dp(4f), dp(4f), fillPaint)
        }
        drawSoundButton(canvas)
    }

    private fun drawSoundButton(canvas: Canvas) {
        fillPaint.color = if (soundEnabled) 0xFFFFE2B8.toInt() else 0xFFEEE7E0.toInt()
        canvas.drawRoundRect(soundRect, dp(12f), dp(12f), fillPaint)
        val left = soundRect.left + dp(10f)
        val cy = soundRect.centerY()
        iconPath.reset()
        iconPath.moveTo(left, cy - dp(4f))
        iconPath.lineTo(left + dp(5f), cy - dp(4f))
        iconPath.lineTo(left + dp(11f), cy - dp(10f))
        iconPath.lineTo(left + dp(11f), cy + dp(10f))
        iconPath.lineTo(left + dp(5f), cy + dp(4f))
        iconPath.lineTo(left, cy + dp(4f))
        iconPath.close()
        fillPaint.color = COCOA
        canvas.drawPath(iconPath, fillPaint)
        strokePaint.color = COCOA
        strokePaint.strokeWidth = dp(2f)
        if (soundEnabled) {
            workRect.set(left + dp(8f), cy - dp(8f), left + dp(21f), cy + dp(8f))
            canvas.drawArc(workRect, -55f, 110f, false, strokePaint)
        } else {
            canvas.drawLine(left + dp(15f), cy - dp(6f), left + dp(23f), cy + dp(6f), strokePaint)
            canvas.drawLine(left + dp(23f), cy - dp(6f), left + dp(15f), cy + dp(6f), strokePaint)
        }
    }

    private fun cellSize(): Float = boardRect.width() / Rules.BOARD_COLS

    private fun plateRadius(): Float = cellSize() * 0.39f

    private fun plateX(index: Int): Float =
        boardRect.left + (index % Rules.BOARD_COLS + 0.5f) * cellSize()

    private fun plateY(index: Int): Float =
        boardRect.top + (index / Rules.BOARD_COLS + 0.5f) * cellSize()

    private fun drawBoard(canvas: Canvas, now: Long) {
        fillPaint.color = PANEL
        canvas.drawRoundRect(boardRect, dp(20f), dp(20f), fillPaint)
        strokePaint.color = PANEL_EDGE
        strokePaint.strokeWidth = dp(2f)
        canvas.drawRoundRect(boardRect, dp(20f), dp(20f), strokePaint)

        val choosingPlate = dragStarted || selectedSlot >= 0
        val hovered = if (dragStarted) plateAt(touchX, touchY) else -1
        for (index in engine.plates.indices) {
            val cx = plateX(index)
            val cy = plateY(index)
            val highlight = choosingPlate && engine.plates[index].isEmpty
            drawPlate(canvas, cx, cy, plateRadius(), highlight, index == hovered)
            val slices = engine.plates[index].slices
            if (slices.isNotEmpty()) {
                drawCake(canvas, cx, cy, plateRadius() * 0.82f, slices)
            }
        }

        // A brief source/destination ring makes each merge readable even with several wedges.
        for (animation in mergeAnimations) {
            if (now < animation.start || now > animation.end) continue
            val pulse = (0.45f + 0.55f * sin((now - animation.start) * Math.PI / MERGE_DURATION)).toFloat()
            strokePaint.color = FLAVORS[animation.flavor]
            strokePaint.alpha = (180 * pulse).toInt()
            strokePaint.strokeWidth = dp(3f)
            for (index in intArrayOf(animation.from, animation.to)) {
                val r = plateRadius() * 0.93f
                canvas.drawCircle(plateX(index), plateY(index), r, strokePaint)
            }
            strokePaint.alpha = 255
        }
    }

    private fun drawPlate(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
        highlight: Boolean,
        hovered: Boolean
    ) {
        ovalRect.set(cx - radius, cy - radius * 0.88f + dp(3f),
            cx + radius, cy + radius * 0.88f + dp(3f))
        fillPaint.color = 0x26000000
        canvas.drawOval(ovalRect, fillPaint)
        ovalRect.offset(0f, -dp(3f))
        fillPaint.color = PLATE
        canvas.drawOval(ovalRect, fillPaint)
        strokePaint.color = if (hovered) ACCENT else if (highlight) GOLD else PLATE_EDGE
        strokePaint.strokeWidth = dp(if (hovered) 4f else if (highlight) 2.5f else 1.3f)
        canvas.drawOval(ovalRect, strokePaint)
    }

    private fun drawWedge(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
        startAngle: Float,
        flavor: Int,
        alpha: Int = 255
    ) {
        ovalRect.set(cx - radius, cy - radius, cx + radius, cy + radius)
        wedgePath.reset()
        wedgePath.moveTo(cx, cy)
        wedgePath.arcTo(ovalRect, startAngle, 60f, false)
        wedgePath.close()
        fillPaint.color = FLAVORS[flavor]
        fillPaint.alpha = alpha
        canvas.drawPath(wedgePath, fillPaint)
        strokePaint.color = 0xFF5A3E36.toInt()
        strokePaint.alpha = alpha
        strokePaint.strokeWidth = (radius * 0.025f).coerceAtLeast(dp(0.7f))
        canvas.drawPath(wedgePath, strokePaint)
        fillPaint.alpha = 255
        strokePaint.alpha = 255
    }

    private fun drawCake(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
        slices: List<Int>,
        alpha: Int = 255,
        scale: Float = 1f
    ) {
        val r = radius * scale
        for (index in slices.indices) {
            drawWedge(canvas, cx, cy, r, -90f + index * 60f, slices[index], alpha)
        }
        fillPaint.color = 0xFFFFF8EC.toInt()
        fillPaint.alpha = alpha
        canvas.drawCircle(cx, cy, r * 0.14f, fillPaint)
        strokePaint.color = 0xFFB98B6E.toInt()
        strokePaint.alpha = alpha
        strokePaint.strokeWidth = dp(1f)
        canvas.drawCircle(cx, cy, r * 0.14f, strokePaint)
        fillPaint.alpha = 255
        strokePaint.alpha = 255
    }

    private fun drawMergeAnimations(canvas: Canvas, now: Long) {
        for (animation in mergeAnimations) {
            if (now < animation.start || now > animation.end) continue
            val raw = ((now - animation.start).toFloat() / MERGE_DURATION).coerceIn(0f, 1f)
            val progress = 1f - (1f - raw) * (1f - raw)
            val sx = plateX(animation.from)
            val sy = plateY(animation.from)
            val dx = plateX(animation.to)
            val dy = plateY(animation.to)
            val cx = sx + (dx - sx) * progress
            val cy = sy + (dy - sy) * progress - sin(raw * Math.PI).toFloat() * dp(14f)
            strokePaint.color = FLAVORS[animation.flavor]
            strokePaint.alpha = (100 * (1f - raw)).toInt()
            strokePaint.strokeWidth = dp(2f)
            canvas.drawLine(sx, sy, cx, cy, strokePaint)
            strokePaint.alpha = 255
            for (slice in 0 until animation.count) {
                drawWedge(canvas, cx, cy, plateRadius() * 0.30f,
                    -90f + slice * 60f, animation.flavor)
            }
        }
    }

    private fun drawServeAnimations(canvas: Canvas, now: Long) {
        for (animation in serveAnimations) {
            if (now < animation.start || now > animation.end) continue
            val progress = ((now - animation.start).toFloat() / SERVE_DURATION).coerceIn(0f, 1f)
            val alpha = (255 * (1f - progress)).toInt()
            val fullCake = List(Rules.SLICES_PER_CAKE) { animation.flavor }
            drawCake(canvas, plateX(animation.plate), plateY(animation.plate),
                plateRadius() * 0.82f, fullCake, alpha, 1f + progress * 0.30f)
            val label = if (animation.combo > 1) {
                "+${animation.points}  x${animation.combo} combo"
            } else {
                "+${animation.points}"
            }
            textPaint.alpha = alpha
            drawText(canvas, label, plateX(animation.plate),
                plateY(animation.plate) - plateRadius() * (0.8f + progress),
                13f, ACCENT, Paint.Align.CENTER, true)
            textPaint.alpha = 255
        }
    }

    private fun drawBoosters(canvas: Canvas) {
        drawBoosterButton(canvas, hammerRect, "Hammer  $hammers", hammers > 0, hammerArmed, true)
        drawBoosterButton(canvas, shuffleRect, "Shuffle  $shuffles", shuffles > 0, false, false)
    }

    private fun drawBoosterButton(
        canvas: Canvas,
        rect: RectF,
        label: String,
        enabled: Boolean,
        armed: Boolean,
        hammerIcon: Boolean
    ) {
        fillPaint.color = when {
            armed -> 0xFFFFD18A.toInt()
            enabled -> 0xFFFFFCF5.toInt()
            else -> DISABLED
        }
        canvas.drawRoundRect(rect, dp(15f), dp(15f), fillPaint)
        strokePaint.color = if (armed) ACCENT else PANEL_EDGE
        strokePaint.strokeWidth = dp(if (armed) 3f else 1.5f)
        canvas.drawRoundRect(rect, dp(15f), dp(15f), strokePaint)
        val color = if (enabled) COCOA else 0xFF9E958F.toInt()
        val ix = rect.left + dp(25f)
        val iy = rect.centerY()
        if (hammerIcon) drawHammerIcon(canvas, ix, iy, color) else drawShuffleIcon(canvas, ix, iy, color)
        drawText(canvas, label, rect.left + dp(46f), iy + sp(5f), 14f, color,
            Paint.Align.LEFT, true)
    }

    private fun drawHammerIcon(canvas: Canvas, x: Float, y: Float, color: Int) {
        strokePaint.color = color
        strokePaint.strokeWidth = dp(4f)
        canvas.drawLine(x - dp(7f), y + dp(10f), x + dp(5f), y - dp(5f), strokePaint)
        canvas.save()
        canvas.rotate(-45f, x + dp(4f), y - dp(7f))
        fillPaint.color = color
        workRect.set(x - dp(6f), y - dp(12f), x + dp(14f), y - dp(3f))
        canvas.drawRoundRect(workRect, dp(2f), dp(2f), fillPaint)
        canvas.restore()
    }

    private fun drawShuffleIcon(canvas: Canvas, x: Float, y: Float, color: Int) {
        strokePaint.color = color
        strokePaint.strokeWidth = dp(2f)
        workRect.set(x - dp(12f), y - dp(12f), x + dp(12f), y + dp(4f))
        canvas.drawArc(workRect, 200f, 210f, false, strokePaint)
        workRect.set(x - dp(12f), y - dp(4f), x + dp(12f), y + dp(12f))
        canvas.drawArc(workRect, 20f, 210f, false, strokePaint)
        fillPaint.color = color
        iconPath.reset()
        iconPath.moveTo(x + dp(12f), y - dp(7f))
        iconPath.lineTo(x + dp(4f), y - dp(11f))
        iconPath.lineTo(x + dp(6f), y - dp(2f))
        iconPath.close()
        canvas.drawPath(iconPath, fillPaint)
        iconPath.reset()
        iconPath.moveTo(x - dp(12f), y + dp(7f))
        iconPath.lineTo(x - dp(4f), y + dp(11f))
        iconPath.lineTo(x - dp(6f), y + dp(2f))
        iconPath.close()
        canvas.drawPath(iconPath, fillPaint)
    }

    private fun slotCenterX(slot: Int): Float = slotRect.left + (slot + 0.5f) * slotRect.width() / 3f

    private fun slotCenterY(): Float = slotRect.top + slotRect.height() * 0.54f

    private fun slotRadius(): Float = minOf(slotRect.height() * 0.30f, slotRect.width() / 6f * 0.65f)

    private fun drawSlots(canvas: Canvas) {
        drawText(canvas, "WAITING CAKES - DRAG OR TAP", slotRect.centerX(), slotRect.top + sp(15f),
            12f, MUTED, Paint.Align.CENTER, true)
        for (slot in 0 until Rules.SLOT_COUNT) {
            val cake = engine.slots[slot] ?: continue
            val cx = slotCenterX(slot)
            val cy = slotCenterY()
            val radius = slotRadius()
            drawPlate(canvas, cx, cy, radius * 1.08f, selectedSlot == slot, false)
            val alpha = if (dragStarted && draggingSlot == slot) 80 else 255
            drawCake(canvas, cx, cy, radius * 0.86f, cake.slices.asList(), alpha)
            if (selectedSlot == slot) {
                strokePaint.color = ACCENT
                strokePaint.strokeWidth = dp(3f)
                canvas.drawCircle(cx, cy, radius * 1.02f, strokePaint)
            }
            drawText(canvas, "${slot + 1}", cx, slotRect.bottom - dp(3f), 11f,
                MUTED, Paint.Align.CENTER)
        }
    }

    private fun drawDragGhost(canvas: Canvas) {
        if (!dragStarted || draggingSlot !in engine.slots.indices) return
        val cake = engine.slots[draggingSlot] ?: return
        drawCake(canvas, touchX, touchY - dp(24f), slotRadius(), cake.slices.asList(), 225, 1.05f)
    }

    private fun drawMessage(canvas: Canvas, now: Long) {
        if (message.isEmpty() || now >= messageUntil) return
        textPaint.textSize = sp(14f)
        val width = textPaint.measureText(message) + dp(28f)
        val bottom = boosterRect.top - dp(5f)
        workRect.set((this.width - width) / 2f, bottom - dp(36f),
            (this.width + width) / 2f, bottom)
        fillPaint.color = 0xE64E342E.toInt()
        canvas.drawRoundRect(workRect, dp(18f), dp(18f), fillPaint)
        drawText(canvas, message, workRect.centerX(), workRect.centerY() + sp(5f),
            14f, Color.WHITE, Paint.Align.CENTER)
    }

    private fun overlayVisible(now: Long): Boolean =
        engine.state != GameState.PLAYING && now >= overlayAt

    private fun drawOverlay(canvas: Canvas) {
        canvas.drawColor(0xD930211C.toInt())
        val panelWidth = minOf(width - dp(36f), dp(360f))
        val panelHeight = dp(300f)
        val left = (width - panelWidth) / 2f
        val top = (height - panelHeight) / 2f
        workRect.set(left, top, left + panelWidth, top + panelHeight)
        fillPaint.color = CREAM
        canvas.drawRoundRect(workRect, dp(26f), dp(26f), fillPaint)
        strokePaint.color = PANEL_EDGE
        strokePaint.strokeWidth = dp(2f)
        canvas.drawRoundRect(workRect, dp(26f), dp(26f), strokePaint)

        val won = engine.state == GameState.WON
        if (won) {
            drawStar(canvas, workRect.centerX(), top + dp(61f), dp(43f), dp(22f))
            drawText(canvas, "Level Complete!", workRect.centerX(), top + dp(132f),
                28f, ACCENT, Paint.Align.CENTER, true)
            drawText(canvas, "A delicious clear!", workRect.centerX(), top + dp(160f),
                15f, MUTED, Paint.Align.CENTER)
        } else {
            drawPlate(canvas, workRect.centerX(), top + dp(62f), dp(38f), false, false)
            strokePaint.color = ACCENT
            strokePaint.strokeWidth = dp(4f)
            canvas.drawLine(workRect.centerX() - dp(18f), top + dp(44f),
                workRect.centerX() + dp(18f), top + dp(80f), strokePaint)
            canvas.drawLine(workRect.centerX() + dp(18f), top + dp(44f),
                workRect.centerX() - dp(18f), top + dp(80f), strokePaint)
            drawText(canvas, "No more room!", workRect.centerX(), top + dp(132f),
                28f, ACCENT, Paint.Align.CENTER, true)
            drawText(canvas, "Try a new arrangement.", workRect.centerX(), top + dp(160f),
                15f, MUTED, Paint.Align.CENTER)
        }
        drawText(canvas, "Score ${engine.score}   Best $highScore", workRect.centerX(), top + dp(190f),
            17f, COCOA, Paint.Align.CENTER, true)
        overlayButtonRect.set(left + dp(28f), top + dp(220f), left + panelWidth - dp(28f), top + dp(274f))
        fillPaint.color = ACCENT
        canvas.drawRoundRect(overlayButtonRect, dp(16f), dp(16f), fillPaint)
        drawText(canvas, if (won) "Next Level" else "Try Again", overlayButtonRect.centerX(),
            overlayButtonRect.centerY() + sp(6f), 19f, Color.WHITE, Paint.Align.CENTER, true)
    }

    private fun drawStar(canvas: Canvas, cx: Float, cy: Float, outer: Float, inner: Float) {
        iconPath.reset()
        for (point in 0 until 16) {
            val radius = if (point % 2 == 0) outer else inner
            val angle = -Math.PI / 2.0 + point * Math.PI / 8.0
            val x = cx + cos(angle).toFloat() * radius
            val y = cy + sin(angle).toFloat() * radius
            if (point == 0) iconPath.moveTo(x, y) else iconPath.lineTo(x, y)
        }
        iconPath.close()
        fillPaint.color = GOLD
        canvas.drawPath(iconPath, fillPaint)
        strokePaint.color = 0xFFE65100.toInt()
        strokePaint.strokeWidth = dp(2f)
        canvas.drawPath(iconPath, strokePaint)
        fillPaint.color = CREAM
        canvas.drawCircle(cx, cy, inner * 0.35f, fillPaint)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x
        val y = event.y
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downX = x
                downY = y
                touchX = x
                touchY = y
                if (overlayVisible(SystemClock.uptimeMillis())) return true
                if (engine.state != GameState.PLAYING) {
                    overlayAt = 0L
                    invalidate()
                    return true
                }
                draggingSlot = if (hammerArmed) -1 else slotAt(x, y)
                dragStarted = false
                parent.requestDisallowInterceptTouchEvent(true)
                return true
            }

            MotionEvent.ACTION_MOVE -> {
                touchX = x
                touchY = y
                if (draggingSlot >= 0 &&
                    (abs(x - downX) > touchSlop || abs(y - downY) > touchSlop)
                ) {
                    dragStarted = true
                    selectedSlot = -1
                }
                if (dragStarted) invalidate()
                return true
            }

            MotionEvent.ACTION_UP -> {
                touchX = x
                touchY = y
                performClick()
                if (overlayVisible(SystemClock.uptimeMillis())) {
                    if (overlayButtonRect.contains(x, y)) {
                        if (engine.state == GameState.WON) advanceLevel() else restartLevel()
                    }
                    resetTouch()
                    return true
                }
                if (engine.state != GameState.PLAYING) {
                    overlayAt = 0L
                    resetTouch()
                    invalidate()
                    return true
                }

                val activeSlot = draggingSlot
                if (activeSlot >= 0) {
                    if (dragStarted) {
                        val plate = plateAt(x, y)
                        if (plate >= 0) placeCake(activeSlot, plate)
                        else showMessage("Drop onto an empty plate")
                    } else {
                        selectedSlot = if (selectedSlot == activeSlot) -1 else activeSlot
                        if (selectedSlot >= 0) showMessage("Cake selected - tap an empty plate")
                    }
                    resetTouch()
                    invalidate()
                    return true
                }

                when {
                    soundRect.contains(x, y) -> toggleSound()
                    hammerRect.contains(x, y) -> toggleHammer()
                    shuffleRect.contains(x, y) -> useShuffle()
                    hammerArmed && plateAt(x, y) >= 0 -> useHammer(plateAt(x, y))
                    selectedSlot >= 0 && plateAt(x, y) >= 0 -> placeCake(selectedSlot, plateAt(x, y))
                    plateAt(x, y) >= 0 -> showMessage("Choose a waiting cake first")
                }
                resetTouch()
                invalidate()
                return true
            }

            MotionEvent.ACTION_CANCEL -> {
                resetTouch()
                invalidate()
                return true
            }
        }
        return super.onTouchEvent(event)
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    private fun resetTouch() {
        draggingSlot = -1
        dragStarted = false
        parent?.requestDisallowInterceptTouchEvent(false)
    }

    private fun slotAt(x: Float, y: Float): Int {
        if (!slotRect.contains(x, y)) return -1
        val slot = ((x - slotRect.left) / (slotRect.width() / Rules.SLOT_COUNT)).toInt()
        return slot.coerceIn(0, Rules.SLOT_COUNT - 1).takeIf { engine.slots[it] != null } ?: -1
    }

    private fun plateAt(x: Float, y: Float): Int {
        if (!boardRect.contains(x, y)) return -1
        val cell = cellSize()
        val col = ((x - boardRect.left) / cell).toInt().coerceIn(0, Rules.BOARD_COLS - 1)
        val row = ((y - boardRect.top) / cell).toInt().coerceIn(0, Rules.BOARD_ROWS - 1)
        return row * Rules.BOARD_COLS + col
    }

    private fun placeCake(slot: Int, plate: Int) {
        if (!engine.canPlace(plate)) {
            showMessage("That plate is occupied - choose an empty one")
            return
        }
        val events = engine.place(slot, plate)
        if (events.isEmpty()) {
            showMessage("That cake cannot be placed there")
            return
        }
        selectedSlot = -1
        hammerArmed = false
        processEvents(events)
        if (engine.score > highScore) highScore = engine.score
        saveProgress()
        invalidate()
    }

    private fun processEvents(events: List<GameEvent>) {
        val now = SystemClock.uptimeMillis()
        var cursor = now
        var lastAnimationEnd = now
        for (event in events) {
            when (event) {
                is GameEvent.Placed -> playTone(ToneGenerator.TONE_PROP_BEEP, 60)
                is GameEvent.Merged -> {
                    val end = cursor + MERGE_DURATION
                    mergeAnimations += MergeAnimation(event.from, event.to, event.flavor,
                        event.count, cursor, end)
                    lastAnimationEnd = maxOf(lastAnimationEnd, end)
                    cursor += 90L
                    playTone(ToneGenerator.TONE_PROP_ACK, 75)
                }
                is GameEvent.Served -> {
                    val end = cursor + SERVE_DURATION
                    serveAnimations += ServeAnimation(event.plate, event.flavor, event.points,
                        event.combo, cursor, end)
                    lastAnimationEnd = maxOf(lastAnimationEnd, end)
                    cursor += 140L
                    playTone(ToneGenerator.TONE_PROP_BEEP2, 120)
                }
                is GameEvent.SlotRefilled -> Unit
                GameEvent.Won -> {
                    overlayAt = maxOf(cursor + 80L, lastAnimationEnd + 60L)
                    val delay = (overlayAt - now).coerceAtLeast(0L)
                    playWinArpeggio(delay)
                }
                GameEvent.Lost -> {
                    overlayAt = now
                    playTone(ToneGenerator.TONE_PROP_NACK, 180)
                }
            }
        }
    }

    private fun toggleHammer() {
        if (hammers <= 0) {
            showMessage("No hammers left this level")
            return
        }
        hammerArmed = !hammerArmed
        selectedSlot = -1
        showMessage(if (hammerArmed) "Tap a plate to clear it" else "Hammer put away")
    }

    private fun useHammer(plate: Int) {
        val cleared = engine.hammer(plate)
        hammerArmed = false
        if (cleared) {
            hammers--
            playTone(ToneGenerator.TONE_PROP_ACK, 90)
            showMessage("Plate cleared")
        } else {
            showMessage("That plate is already empty")
        }
    }

    private fun useShuffle() {
        if (shuffles <= 0) {
            showMessage("No shuffles left this level")
            return
        }
        engine.shuffle()
        shuffles--
        selectedSlot = -1
        playTone(ToneGenerator.TONE_PROP_BEEP, 70)
        showMessage("Waiting cakes shuffled")
    }

    private fun advanceLevel() {
        engine.nextLevel()
        resetForNewAttempt()
        saveProgress()
        playTone(ToneGenerator.TONE_PROP_BEEP2, 100)
    }

    private fun restartLevel() {
        engine = Engine(engine.level, System.nanoTime())
        resetForNewAttempt()
        saveProgress()
    }

    private fun resetForNewAttempt() {
        hammers = BOOSTERS_PER_LEVEL
        shuffles = BOOSTERS_PER_LEVEL
        hammerArmed = false
        selectedSlot = -1
        mergeAnimations.clear()
        serveAnimations.clear()
        overlayAt = 0L
        messageUntil = 0L
        invalidate()
    }

    private fun showMessage(value: String) {
        message = value
        messageUntil = SystemClock.uptimeMillis() + MESSAGE_DURATION
        postInvalidateOnAnimation()
        postInvalidateDelayed(MESSAGE_DURATION + 20L)
    }

    private fun toggleSound() {
        soundEnabled = !soundEnabled
        preferences.edit().putBoolean(KEY_SOUND, soundEnabled).apply()
        if (!soundEnabled) soundHandler.removeCallbacksAndMessages(null)
        showMessage(if (soundEnabled) "Sound on" else "Sound off")
        if (soundEnabled) playTone(ToneGenerator.TONE_PROP_BEEP, 60)
    }

    private fun playTone(tone: Int, durationMs: Int, delayMs: Long = 0L) {
        if (!soundEnabled || released) return
        soundHandler.postDelayed({
            if (!released && soundEnabled) {
                val generator = toneGenerator ?: ToneGenerator(AudioManager.STREAM_MUSIC, 70).also {
                    toneGenerator = it
                }
                generator.startTone(tone, durationMs)
            }
        }, delayMs)
    }

    private fun playWinArpeggio(delayMs: Long) {
        playTone(ToneGenerator.TONE_PROP_BEEP, 100, delayMs)
        playTone(ToneGenerator.TONE_PROP_ACK, 100, delayMs + 110L)
        playTone(ToneGenerator.TONE_PROP_BEEP2, 160, delayMs + 220L)
    }

    fun saveProgress() {
        if (engine.score > highScore) highScore = engine.score
        preferences.edit()
            .putInt(KEY_LEVEL, engine.level)
            .putInt(KEY_HIGH_SCORE, highScore)
            .putBoolean(KEY_SOUND, soundEnabled)
            .apply()
    }

    fun release() {
        released = true
        soundHandler.removeCallbacksAndMessages(null)
        toneGenerator?.release()
        toneGenerator = null
        saveProgress()
    }
}

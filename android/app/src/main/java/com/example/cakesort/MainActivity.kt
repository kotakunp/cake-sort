package com.example.cakesort

import android.app.Activity
import android.os.Bundle

/** Hosts the single custom game view and owns its persistence/lifecycle hooks. */
class MainActivity : Activity() {
    private lateinit var gameView: GameView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        gameView = GameView(this)
        setContentView(gameView)
    }

    override fun onPause() {
        gameView.saveProgress()
        super.onPause()
    }

    override fun onDestroy() {
        gameView.release()
        super.onDestroy()
    }
}
